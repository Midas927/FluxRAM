const year = document.getElementById("current-year");
if (year) year.textContent = String(new Date().getFullYear());

const progress = document.getElementById("scroll-progress");
let progressQueued = false;

function updateScrollProgress() {
  const scrollable = Math.max(1, document.documentElement.scrollHeight - window.innerHeight);
  const ratio = Math.min(1, Math.max(0, window.scrollY / scrollable));
  if (progress) progress.style.width = `${ratio * 100}%`;
  progressQueued = false;
}

window.addEventListener("scroll", () => {
  if (!progressQueued) {
    progressQueued = true;
    requestAnimationFrame(updateScrollProgress);
  }
}, { passive: true });
updateScrollProgress();

const revealItems = document.querySelectorAll("[data-reveal]");
if ("IntersectionObserver" in window) {
  const revealObserver = new IntersectionObserver((entries, observer) => {
    for (const entry of entries) {
      if (!entry.isIntersecting) continue;
      entry.target.classList.add("is-visible");
      observer.unobserve(entry.target);
    }
  }, { threshold: 0.14, rootMargin: "0px 0px -40px" });
  revealItems.forEach(item => revealObserver.observe(item));
} else {
  revealItems.forEach(item => item.classList.add("is-visible"));
}

const profiles = {
  daily: {
    overline: "日常、办公与浏览器",
    title: "Daily",
    description: "最低打扰，优先保护正在使用的软件，适合文档、浏览器和普通多任务。",
    threshold: "280 MB",
    targets: "2 个",
    protection: "前台与活跃进程",
    active: 14,
    protected: 7,
    cold: 3
  },
  gaming: {
    overline: "游戏 PC 与 Windows 掌机",
    title: "Gaming",
    description: "更积极地寻找可回收空间，同时保护游戏、启动器、掌机控制中心和活跃进程。",
    threshold: "96 MB",
    targets: "7 个",
    protection: "游戏与掌机",
    active: 11,
    protected: 8,
    cold: 8
  },
  extreme: {
    overline: "本地 AI、剪辑与重负载",
    title: "Extreme",
    description: "将候选门槛降到最低，并提供需主动确认的深度释放；系统和保护应用仍被排除。",
    threshold: "64 MB",
    targets: "动态",
    protection: "系统与自定义名单",
    active: 8,
    protected: 6,
    cold: 14
  }
};

const profileFields = {
  overline: document.getElementById("profile-overline"),
  title: document.getElementById("profile-title"),
  description: document.getElementById("profile-description"),
  threshold: document.getElementById("profile-threshold"),
  targets: document.getElementById("profile-targets"),
  protection: document.getElementById("profile-protection"),
  labMode: document.getElementById("lab-mode")
};

const board = document.getElementById("profile-memory-board");
const profileButtons = document.querySelectorAll("[data-profile]");
const profilePanel = document.getElementById("profile-panel");

function renderMemoryBoard(profile) {
  if (!board) return;
  board.replaceChildren();
  const total = 40;
  for (let index = 0; index < total; index += 1) {
    const cell = document.createElement("span");
    cell.className = "memory-cell";
    const pattern = (index * 17 + profile.cold * 7) % total;
    if (pattern < profile.protected) cell.classList.add("is-protected");
    else if (pattern < profile.protected + profile.cold) cell.classList.add("is-cold");
    else if (pattern < profile.protected + profile.cold + profile.active) cell.classList.add("is-active");
    else cell.classList.add("is-empty");
    cell.style.transitionDelay = `${(index % 8) * 18}ms`;
    board.appendChild(cell);
  }
}

function selectProfile(name) {
  const profile = profiles[name];
  if (!profile) return;

  for (const button of profileButtons) {
    const selected = button.dataset.profile === name;
    button.classList.toggle("is-active", selected);
    button.setAttribute("aria-selected", String(selected));
    button.tabIndex = selected ? 0 : -1;
    if (selected && profilePanel) profilePanel.setAttribute("aria-labelledby", button.id);
  }

  profileFields.overline.textContent = profile.overline;
  profileFields.title.textContent = profile.title;
  profileFields.description.textContent = profile.description;
  profileFields.threshold.textContent = profile.threshold;
  profileFields.targets.textContent = profile.targets;
  profileFields.protection.textContent = profile.protection;
  profileFields.labMode.textContent = profile.title.toUpperCase();
  renderMemoryBoard(profile);
}

for (const button of profileButtons) {
  button.addEventListener("click", () => selectProfile(button.dataset.profile));
  button.addEventListener("keydown", event => {
    const keys = ["ArrowLeft", "ArrowRight", "Home", "End"];
    if (!keys.includes(event.key)) return;
    event.preventDefault();
    const buttons = Array.from(profileButtons);
    const current = buttons.indexOf(button);
    let next = current;
    if (event.key === "ArrowLeft") next = (current - 1 + buttons.length) % buttons.length;
    if (event.key === "ArrowRight") next = (current + 1) % buttons.length;
    if (event.key === "Home") next = 0;
    if (event.key === "End") next = buttons.length - 1;
    buttons[next].focus();
    buttons[next].click();
  });
}
selectProfile("gaming");

const canvas = document.getElementById("memory-field");
const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");

if (canvas) {
  const context = canvas.getContext("2d", { alpha: false });
  const pointer = { x: 0, y: 0, active: false };
  let blocks = [];
  let canvasWidth = 0;
  let canvasHeight = 0;
  let pixelRatio = 1;
  let animationFrame = 0;
  let lastTelemetryUpdate = 0;

  function seededValue(column, row) {
    const value = Math.sin(column * 91.17 + row * 41.73) * 43758.5453;
    return value - Math.floor(value);
  }

  function resizeCanvas() {
    const bounds = canvas.getBoundingClientRect();
    canvasWidth = Math.max(1, Math.round(bounds.width));
    canvasHeight = Math.max(1, Math.round(bounds.height));
    pixelRatio = Math.min(1.5, window.devicePixelRatio || 1);
    canvas.width = Math.round(canvasWidth * pixelRatio);
    canvas.height = Math.round(canvasHeight * pixelRatio);
    context.setTransform(pixelRatio, 0, 0, pixelRatio, 0, 0);

    const cellWidth = canvasWidth < 700 ? 30 : 38;
    const cellHeight = canvasWidth < 700 ? 19 : 23;
    const gap = canvasWidth < 700 ? 6 : 8;
    const columns = Math.ceil(canvasWidth / (cellWidth + gap)) + 2;
    const rows = Math.ceil(canvasHeight / (cellHeight + gap)) + 2;
    blocks = [];

    for (let row = 0; row < rows; row += 1) {
      for (let column = 0; column < columns; column += 1) {
        const seed = seededValue(column, row);
        blocks.push({
          x: column * (cellWidth + gap) - cellWidth,
          y: row * (cellHeight + gap) - cellHeight,
          width: cellWidth,
          height: cellHeight,
          seed,
          status: seed > 0.91 ? "protected" : seed > 0.76 ? "cold" : seed > 0.28 ? "active" : "empty"
        });
      }
    }
    drawField(performance.now());
  }

  function blockColor(block, intensity, release) {
    if (release > 0.72 && block.status === "cold") return `rgba(53, 212, 162, ${0.2 + intensity * 0.45})`;
    if (block.status === "protected") return `rgba(45, 153, 116, ${0.17 + intensity * 0.22})`;
    if (block.status === "cold") return `rgba(202, 97, 68, ${0.1 + intensity * 0.24})`;
    if (block.status === "active") return `rgba(105, 123, 131, ${0.09 + intensity * 0.2})`;
    return `rgba(43, 55, 62, ${0.08 + intensity * 0.1})`;
  }

  function updateTelemetry(time) {
    if (time - lastTelemetryUpdate < 300) return;
    lastTelemetryUpdate = time;
    const load = document.getElementById("demo-load");
    const trim = document.getElementById("demo-trim");
    const state = document.getElementById("demo-state");
    const cycle = (time / 1000) % 8;
    if (load) load.textContent = `${(74.8 + Math.sin(time / 1700) * 0.35).toFixed(1)}%`;
    if (trim) trim.textContent = `${Math.round(218 + Math.sin(time / 1300) * 16)} MB`;
    if (state) state.textContent = cycle > 5.8 && cycle < 7.2 ? "释放低风险候选" : "扫描冷后台";
  }

  function drawField(time) {
    context.fillStyle = "#090d12";
    context.fillRect(0, 0, canvasWidth, canvasHeight);

    const waveX = ((time / 30) % (canvasWidth + 260)) - 130;
    const releasePhase = (time % 8000) / 8000;
    const releaseX = releasePhase * (canvasWidth + 360) - 180;

    for (const block of blocks) {
      const centerX = block.x + block.width / 2;
      const centerY = block.y + block.height / 2;
      const waveDistance = Math.abs(centerX - waveX);
      const releaseDistance = Math.abs(centerX - releaseX);
      let intensity = Math.max(0, 1 - waveDistance / 170) * 0.55;

      if (pointer.active) {
        const distance = Math.hypot(centerX - pointer.x, centerY - pointer.y);
        intensity += Math.max(0, 1 - distance / 180) * 0.6;
      }

      const release = Math.max(0, 1 - releaseDistance / 95);
      context.fillStyle = blockColor(block, Math.min(1, intensity), release);
      context.fillRect(block.x, block.y, block.width, block.height);

      if (block.status === "protected") {
        context.strokeStyle = `rgba(75, 222, 169, ${0.1 + intensity * 0.25})`;
        context.lineWidth = 1;
        context.strokeRect(block.x + 0.5, block.y + 0.5, block.width - 1, block.height - 1);
      }
    }

    context.fillStyle = "rgba(53, 212, 162, 0.28)";
    context.fillRect(waveX, 0, 1, canvasHeight);
    updateTelemetry(time);

    if (!reducedMotion.matches) animationFrame = requestAnimationFrame(drawField);
  }

  canvas.addEventListener("pointermove", event => {
    const bounds = canvas.getBoundingClientRect();
    pointer.x = event.clientX - bounds.left;
    pointer.y = event.clientY - bounds.top;
    pointer.active = true;
  });
  canvas.addEventListener("pointerleave", () => { pointer.active = false; });

  const resizeObserver = new ResizeObserver(resizeCanvas);
  resizeObserver.observe(canvas);
  reducedMotion.addEventListener("change", () => {
    cancelAnimationFrame(animationFrame);
    drawField(performance.now());
  });
}
