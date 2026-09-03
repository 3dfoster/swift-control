const portRanges = [
  55995, 55996, 55997, 55998, 55999,
  56955, 56956, 56957, 56958, 56959,
];

const requestTimeoutMs = 2_000;
let completed = false;

function decodeMessage(data) {
  return String(data).replaceAll("\0", "").trim();
}

function send(socket, request) {
  // AcerSense uses this four-character prefix to identify an allowed client.
  socket.send(`ACER${JSON.stringify(request)}`);
}

function connect(port) {
  return new Promise((resolve) => {
    const socket = new WebSocket(`ws://localhost:${port}`);
    const timer = setTimeout(() => {
      socket.close();
      resolve(null);
    }, requestTimeoutMs);

    let capability;

    socket.addEventListener("open", () => {
      send(socket, { Function: "GET_ULTRON_LIGHTING_CAPABILITY" });
    });

    socket.addEventListener("message", (event) => {
      const text = decodeMessage(event.data);
      if (text === "Allowed Client") {
        return;
      }

      let response;
      try {
        response = JSON.parse(text);
      } catch {
        return;
      }

      if (response.request === "GET_ULTRON_LIGHTING_CAPABILITY") {
        capability = response;
        const devices = response.data?.devices ?? [];
        const id = devices.includes(5) ? 5 : devices.includes(6) ? 6 : undefined;
        send(socket, {
          Function: "GET_ULTRON_LIGHTING_STATUS",
          ...(id === undefined ? {} : { Parameter: { id } }),
        });
        return;
      }

      if (response.request === "GET_ULTRON_LIGHTING_STATUS") {
        clearTimeout(timer);
        completed = true;
        resolve({ port, capability, status: response });
        socket.close();
      }
    });

    socket.addEventListener("error", () => {
      clearTimeout(timer);
      resolve(null);
    });
  });
}

for (const port of portRanges) {
  const result = await connect(port);
  if (result) {
    console.log(JSON.stringify(result, null, 2));
    process.exitCode = 0;
    break;
  }
}

if (!completed) {
  console.error("No authorized Acer lighting service responded.");
  process.exitCode = 1;
}
