const eventList = document.querySelector("#event-list");
const serviceState = document.querySelector("#service-state");
const servicePill = document.querySelector("#service-pill");
const eventCount = document.querySelector("#event-count");
const eventLabels = {
  "Webhook accepted": "收到 Webex 事件",
  "Webhook rejected": "Webhook 驗證失敗",
  "Text processed": "Gemini 已產生回覆",
  "Reply sent": "已傳回 Webex",
  "Message failed": "訊息處理失敗",
  "Error reply sent": "已傳送錯誤提示",
  "Error reply failed": "錯誤提示傳送失敗",
  "Message ignored": "訊息已略過",
};

function escapeHtml(value) {
  return String(value).replace(/[&<>"']/g, (char) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[char]);
}

function formatTime(value) {
  return new Intl.DateTimeFormat("zh-TW", { hour: "2-digit", minute: "2-digit", second: "2-digit" }).format(new Date(value));
}

function updateStages(settings) {
  for (const stage of document.querySelectorAll(".stage-state")) {
    const configured = stage.dataset.requires.split(",").every((name) => settings[name]);
    stage.classList.toggle("ready", configured);
    stage.classList.toggle("missing", !configured);
    stage.textContent = configured ? "已設定" : "待設定";
  }
}

function renderEvents(events) {
  eventCount.textContent = events.length;
  if (!events.length) {
    eventList.innerHTML = '<div class="empty-state"><i data-lucide="inbox"></i><p>尚無訊息活動</p></div>';
    return;
  }
  eventList.innerHTML = events.map((event) => `
    <article class="event-row ${escapeHtml(event.status)}">
      <span class="event-marker"></span>
      <div class="event-copy">
        <strong>${escapeHtml(eventLabels[event.kind] || event.kind)}</strong>
        <span>${escapeHtml(event.detail)}</span>
      </div>
      <time>${formatTime(event.time)}</time>
    </article>`).join("");
}

async function refreshDashboard() {
  try {
    const [statusResponse, eventsResponse] = await Promise.all([
      fetch("/api/status", { cache: "no-store" }),
      fetch("/api/events", { cache: "no-store" }),
    ]);
    if (!statusResponse.ok || !eventsResponse.ok) throw new Error("status unavailable");
    const [status, recentEvents] = await Promise.all([statusResponse.json(), eventsResponse.json()]);
    const ready = status.state === "ready";
    serviceState.textContent = ready ? "設定完整" : "缺少必要設定";
    servicePill.className = `service-status ${ready ? "ready" : "missing"}`;
    document.querySelector("#chat-model").textContent = status.models.chat;
    document.querySelector("#last-refresh").textContent = `更新於 ${new Date().toLocaleTimeString("zh-TW")}`;
    updateStages(status.settings);
    renderEvents(recentEvents);
  } catch {
    serviceState.textContent = "狀態暫不可用";
    servicePill.className = "service-status offline";
  }
  if (window.lucide) window.lucide.createIcons();
}

document.querySelector("#refresh-events").addEventListener("click", refreshDashboard);
document.querySelector("#footer-time").textContent = `${new Date().toLocaleTimeString("zh-TW", { hour: "2-digit", minute: "2-digit" })}`;
refreshDashboard();
setInterval(refreshDashboard, 8000);