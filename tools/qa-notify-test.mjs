import crypto from "node:crypto";

process.env.NODE_TLS_REJECT_UNAUTHORIZED = "0";
const keyA = Buffer.from("A6052DC8A6E44210", "utf8");
const keyB = "AB252AB73BED1CDB";
const question = "AcerQuickAccessFunctionalityTests_HandshakingQuestion";

function encrypt(text) {
  const cipher = crypto.createCipheriv("aes-128-ecb", keyA, null);
  cipher.setAutoPadding(true);
  return Buffer.concat([cipher.update(text, "utf8"), cipher.final()]).toString("base64");
}

function auth(socket) {
  socket.send(JSON.stringify({
    PacketType: 1,
    Version: 1,
    Session: crypto.randomUUID(),
    Data: encrypt(JSON.stringify({ Question: question, Key: keyB })),
  }));
}

function request(socket, action, value) {
  const packet = {
    PacketType: 2,
    Version: 1,
    Session: crypto.randomUUID(),
    Command: "SystemUsageControl",
    Action: action,
  };
  if (action === "Set") packet.Param1 = value;
  socket.send(JSON.stringify(packet));
}

const observer = new WebSocket("wss://localhost:5141");
const setter = new WebSocket("wss://localhost:5141");
let observerReady = false;
let setterReady = false;
let original = null;
let target = null;
let restored = false;
const observed = [];

function begin() {
  if (observerReady && setterReady && original === null) request(observer, "Get");
}

observer.addEventListener("open", () => auth(observer));
setter.addEventListener("open", () => auth(setter));

observer.addEventListener("message", event => {
  const packet = JSON.parse(event.data);
  if (packet.PacketType === 1) {
    observerReady = true;
    begin();
    return;
  }
  observed.push(packet);
  if (packet.Action === "Get" && original === null) {
    original = packet.Result.Value;
    target = (original + 1) % 3;
    request(setter, "Set", target);
  }
});

setter.addEventListener("message", event => {
  const packet = JSON.parse(event.data);
  if (packet.PacketType === 1) {
    setterReady = true;
    begin();
    return;
  }
  if (packet.Action === "Set" && !restored) {
    restored = true;
    setTimeout(() => request(setter, "Set", original), 700);
    setTimeout(() => {
      console.log(JSON.stringify({ original, target, observerMessages: observed }, null, 2));
      observer.close();
      setter.close();
    }, 2200);
  }
});

setTimeout(() => {
  console.error("Notify test timed out.");
  observer.close();
  setter.close();
  process.exitCode = 1;
}, 8000).unref();
