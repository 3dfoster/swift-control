import crypto from "node:crypto";

const quickAccess = process.argv.includes("--quick-access");
const osdTest = process.argv.includes("--osd-test");
const serviceUrl = quickAccess ? "wss://localhost:5141" : "wss://localhost:4343";
const question = quickAccess
  ? "AcerQuickAccessFunctionalityTests_HandshakingQuestion"
  : "AcerCareCenterFunctionalityTests_HandshakingQuestion";
const keyA = Buffer.from("A6052DC8A6E44210", "utf8");
const keyB = "AB252AB73BED1CDB";

function encryptAesEcb(plaintext, key) {
  const cipher = crypto.createCipheriv("aes-128-ecb", key, null);
  cipher.setAutoPadding(true);
  return Buffer.concat([cipher.update(plaintext, "utf8"), cipher.final()]).toString("base64");
}

function packet(command) {
  const isOsdCommand = osdTest && command === "SystemUsageOSD";
  const request = {
    PacketType: 2,
    Version: 1,
    Session: crypto.randomUUID(),
    Command: command,
    Action: osdTest ? "Set" : "Get",
  };
  if (osdTest) request.Param1 = isOsdCommand ? 4 : false;
  return request;
}

// Acer's localhost service uses its own certificate. This override is scoped to
// this short-lived diagnostic process and never affects system certificate policy.
process.env.NODE_TLS_REJECT_UNAUTHORIZED = "0";

const socket = new WebSocket(serviceUrl);
const timeout = setTimeout(() => {
  console.error("Timed out waiting for Acer Care Center.");
  socket.close();
  process.exitCode = 1;
}, 10_000);

let authenticated = false;
const pending = new Set(quickAccess
  ? (osdTest
      ? ["SenseAppStatus"]
      : ["SystemUsageModes", "SystemUsageControl", "SystemUsageModeCapability", "USBChargeSwitch", "USBCharge"])
  : ["BatteryHealthy", "BatteryInformation", "BatteryStatus"]);

socket.addEventListener("open", () => {
  const session = crypto.randomUUID();
  socket.send(JSON.stringify({
    PacketType: 1,
    Session: session,
    Version: 1,
    Data: encryptAesEcb(JSON.stringify({ Question: question, Key: keyB }), keyA),
  }));
});

socket.addEventListener("message", (event) => {
  const response = JSON.parse(event.data);

  if (!authenticated && response.PacketType === 1) {
    authenticated = true;
    for (const command of pending) {
      socket.send(JSON.stringify(packet(command)));
    }
    return;
  }

  if (osdTest && response.Command === "SenseAppStatus") {
    pending.delete("SenseAppStatus");
    socket.send(JSON.stringify(packet("SystemUsageOSD")));
    setTimeout(() => {
      clearTimeout(timeout);
      socket.close();
    }, 2_000);
    return;
  }

  if (pending.has(response.Command)) {
    console.log(JSON.stringify(response, null, 2));
    pending.delete(response.Command);
  }

  if (pending.size === 0) {
    clearTimeout(timeout);
    socket.close();
  }
});

socket.addEventListener("error", (event) => {
  clearTimeout(timeout);
  console.error("Acer Care Center connection failed:", event.message ?? "WebSocket error");
  process.exitCode = 1;
});
