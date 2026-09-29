const eventList = document.querySelector("#event-list");
const serviceState = document.querySelector("#service-state");
const servicePill = document.querySelector("#service-pill");
const servicePillText = document.querySelector("#service-pill-text");
const uptime = document.querySelector("#uptime");

function setConfigState(name, configured) {
  const item = document.querySelector(`[data-setting="${name}"] .config-state`);
  if (!item) return;
  item.classList.toggle("ready", configured);
  item.classList.toggle("warning", !configured);
  item.querySelector("span:last-child").textContent = configured ? "CONFIGURED" : "MISSING";
}

function formatUptime(seconds) {
  const hours = Math.floor(seconds / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  const secs = seconds % 60;
  return `${String(hours).padStart(2, "0")}h ${String(minutes).padStart(2, "0")}m ${String(secs).padStart(2, "0")}s`;
}

function formatTime(value) {
  return new Intl.DateTimeFormat(undefined, { hour: "2-digit", minute: "2-digit", second: "2-digit" }).format(new Date(value));
}

function renderEvents(events) {
  if (!events.length) {
    eventList.innerHTML = '<div class="empty-state"><span class="empty-mark">—</span><p>Waiting for webhook events</p><span>New delivery diagnostics will appear here.</span></div>';
    return;
  }
  eventList.innerHTML = events.map((event) => `
    <article class="event-row ${event.status}">
      <span class="event-marker"></span>
      <div><div class="event-kind">${escapeHtml(event.kind)}</div><div class="event-detail">${escapeHtml(event.detail)}</div></div>
      <time class="event-time">${formatTime(event.time)}</time>
    </article>`).join("");
}

function escapeHtml(value) {
  return String(value).replace(/[&<>"']/g, (char) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[char]);
}

async function refreshDashboard() {
  try {
    const [statusResponse, eventsResponse] = await Promise.all([
      fetch("/api/status", { cache: "no-store" }),
      fetch("/api/events", { cache: "no-store" }),
    ]);
    if (!statusResponse.ok || !eventsResponse.ok) throw new Error("Could not load service status.");
    const [status, recentEvents] = await Promise.all([statusResponse.json(), eventsResponse.json()]);
    const ready = status.state === "ready";
    serviceState.textContent = ready ? "All systems configured" : "Configuration needed";
    servicePill.className = `state-pill ${ready ? "ready" : "warning"}`;
    servicePillText.textContent = ready ? "OPERATIONAL" : "ATTENTION";
    uptime.textContent = formatUptime(status.uptimeSeconds);
    document.querySelector("#event-count").textContent = status.webhookEvents;
    document.querySelector("#chat-model").textContent = status.models.chat;
    document.querySelector("#last-refresh").textContent = `UPDATED ${new Date().toLocaleTimeString()}`;
    for (const [key, value] of Object.entries(status.settings)) setConfigState(key, value);
    renderEvents(recentEvents);
    if (window.lucide) window.lucide.createIcons();
  } catch (error) {
    serviceState.textContent = "Service status unavailable";
    servicePill.className = "state-pill error";
    servicePillText.textContent = "OFFLINE";
  }
}

document.querySelector("#test-gemini").addEventListener("click", async (event) => {
  const button = event.currentTarget;
  const result = document.querySelector("#test-result");
  button.disabled = true;
  result.className = "test-result";
  result.textContent = "Contacting Gemini…";
  try {
    const response = await fetch("/api/gemini/check", { method: "POST" });
    const data = await response.json();
    if (!response.ok) throw new Error(data.detail || "Connection test failed.");
    result.textContent = `Connected · ${data.reply}`;
  } catch (error) {
    result.className = "test-result error";
    result.textContent = error.message;
  } finally {
    button.disabled = false;
    refreshDashboard();
  }
});

document.querySelector("#refresh-events").addEventListener("click", refreshDashboard);
document.querySelector("#footer-time").textContent = `${new Date().toISOString().slice(11, 19)} UTC`;
refreshDashboard();
setInterval(refreshDashboard, 8000);