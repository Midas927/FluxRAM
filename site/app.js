const year = document.getElementById("current-year");
if (year) year.textContent = String(new Date().getFullYear());

const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

const memoryCanvas = document.getElementById("hero-memory-canvas");
const signalHero = document.querySelector(".hero-signal");

if (memoryCanvas && signalHero) {
  const context = memoryCanvas.getContext("2d", { alpha: false });
  let animationFrame = 0;
  let lastFrameAt = 0;
  let heroVisible = true;
  let canvasWidth = 0;
  let canvasHeight = 0;
  let pixelRatio = 1;
  let blocks = [];
  const pointer = { x: 0, y: 0, active: false };

  function seededValue(column, row) {
    const value = Math.sin(column * 91.17 + row * 41.73) * 43758.5453;
    return value - Math.floor(value);
  }

  function resizeMemoryCanvas() {
    const bounds = memoryCanvas.getBoundingClientRect();
    pixelRatio = Math.min(window.devicePixelRatio || 1, 2);
    canvasWidth = Math.max(1, Math.floor(bounds.width));
    canvasHeight = Math.max(1, Math.floor(bounds.height));
    memoryCanvas.width = Math.floor(canvasWidth * pixelRatio);
    memoryCanvas.height = Math.floor(canvasHeight * pixelRatio);
    context.setTransform(pixelRatio, 0, 0, pixelRatio, 0, 0);

    const cellWidth = 30;
    const cellHeight = 19;
    const gap = 6;
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
          status: seed > 0.91 ? "protected" : seed > 0.76 ? "cold" : seed > 0.28 ? "active" : "empty"
        });
      }
    }

    drawMemoryField(0);
  }

  function blockColor(block, intensity, release) {
    if (release > 0.72 && block.status === "cold") return `rgba(42, 185, 130, ${0.18 + intensity * 0.34})`;
    if (block.status === "protected") return `rgba(34, 126, 93, ${0.12 + intensity * 0.22})`;
    if (block.status === "cold") return `rgba(116, 89, 50, ${0.07 + intensity * 0.14})`;
    if (block.status === "active") return `rgba(58, 78, 89, ${0.08 + intensity * 0.15})`;
    return `rgba(27, 39, 46, ${0.11 + intensity * 0.08})`;
  }

  function drawMemoryField(time) {
    context.fillStyle = "#081015";
    context.fillRect(0, 0, canvasWidth, canvasHeight);

    const waveX = ((time / 42) % (canvasWidth + 260)) - 130;
    const releaseX = ((time % 9000) / 9000) * (canvasWidth + 360) - 180;

    for (const block of blocks) {
      const centerX = block.x + block.width / 2;
      const centerY = block.y + block.height / 2;
      let intensity = Math.max(0, 1 - Math.abs(centerX - waveX) / 180) * 0.48;

      if (pointer.active) {
        const distance = Math.hypot(centerX - pointer.x, centerY - pointer.y);
        intensity += Math.max(0, 1 - distance / 190) * 0.42;
      }

      const release = Math.max(0, 1 - Math.abs(centerX - releaseX) / 100);
      context.fillStyle = blockColor(block, Math.min(1, intensity), release);
      context.fillRect(block.x, block.y, block.width, block.height);

      if (block.status === "protected") {
        context.strokeStyle = `rgba(42, 185, 130, ${0.08 + intensity * 0.2})`;
        context.lineWidth = 1;
        context.strokeRect(block.x + 0.5, block.y + 0.5, block.width - 1, block.height - 1);
      }
    }

    context.strokeStyle = "rgba(122, 151, 159, 0.13)";
    context.lineWidth = 1;
    context.beginPath();
    context.moveTo(canvasWidth * 0.58 + 0.5, 0);
    context.lineTo(canvasWidth * 0.58 + 0.5, canvasHeight);
    context.moveTo(0, canvasHeight * 0.63 + 0.5);
    context.lineTo(canvasWidth, canvasHeight * 0.63 + 0.5);
    context.stroke();

    context.fillStyle = "rgba(42, 185, 130, 0.22)";
    context.fillRect(waveX, 0, 1, canvasHeight);
  }

  function renderMemoryField(time) {
    if (!heroVisible || document.hidden) {
      animationFrame = 0;
      return;
    }
    if (time - lastFrameAt >= 33) {
      drawMemoryField(time);
      lastFrameAt = time;
    }
    animationFrame = window.requestAnimationFrame(renderMemoryField);
  }

  function beginMemoryField() {
    if (reducedMotion || animationFrame || !heroVisible || document.hidden) return;
    animationFrame = window.requestAnimationFrame(renderMemoryField);
  }

  const resizeObserver = new ResizeObserver(resizeMemoryCanvas);
  resizeObserver.observe(memoryCanvas);
  signalHero.addEventListener("pointermove", event => {
    const bounds = memoryCanvas.getBoundingClientRect();
    pointer.x = event.clientX - bounds.left;
    pointer.y = event.clientY - bounds.top;
    pointer.active = true;
  });
  signalHero.addEventListener("pointerleave", () => {
    pointer.active = false;
  });
  document.addEventListener("visibilitychange", () => {
    if (!document.hidden) beginMemoryField();
  });
  const heroObserver = new IntersectionObserver(entries => {
    heroVisible = entries.some(entry => entry.isIntersecting);
    if (heroVisible) beginMemoryField();
  }, { threshold: 0.05 });
  heroObserver.observe(signalHero);
  resizeMemoryCanvas();
  beginMemoryField();
}

const revealItems = document.querySelectorAll(".reveal");

if (reducedMotion || !("IntersectionObserver" in window)) {
  revealItems.forEach(item => item.classList.add("is-visible"));
} else {
  const revealObserver = new IntersectionObserver((entries, observer) => {
    entries.forEach(entry => {
      if (!entry.isIntersecting) return;
      entry.target.classList.add("is-visible");
      observer.unobserve(entry.target);
    });
  }, { threshold: 0.16, rootMargin: "0px 0px -36px" });
  revealItems.forEach(item => revealObserver.observe(item));
}

const tagline = document.querySelector("[data-tagline]");
if (tagline && !reducedMotion && "IntersectionObserver" in window) {
  const lines = tagline.querySelectorAll("span");
  const taglineObserver = new IntersectionObserver((entries, observer) => {
    entries.forEach(entry => {
      if (!entry.isIntersecting) return;
      lines.forEach((line, index) => {
        window.setTimeout(() => line.classList.add("is-lit"), index * 170);
      });
      observer.unobserve(entry.target);
    });
  }, { threshold: 0.2 });
  taglineObserver.observe(tagline);
}

document.querySelectorAll(".mobile-menu a[href^='#']").forEach(link => {
  link.addEventListener("click", () => {
    const menu = link.closest("details");
    if (menu) menu.open = false;
  });
});

document.querySelectorAll(".faq-list details").forEach(item => {
  item.addEventListener("toggle", () => {
    if (!item.open) return;
    document.querySelectorAll(".faq-list details[open]").forEach(other => {
      if (other !== item) other.open = false;
    });
  });
});
