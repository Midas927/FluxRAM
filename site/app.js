const year = document.getElementById("current-year");
if (year) year.textContent = String(new Date().getFullYear());

const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

const memoryCanvas = document.getElementById("hero-memory-canvas");
const signalHero = document.querySelector(".hero-signal");

if (memoryCanvas && signalHero) {
  const context = memoryCanvas.getContext("2d");
  let animationFrame = 0;
  let lastFrameAt = 0;
  let heroVisible = true;
  let canvasWidth = 0;
  let canvasHeight = 0;
  let pixelRatio = 1;

  const palette = ["#2ab982", "#2f67bd", "#2ab982", "#9ba9a8", "#c76a35"];

  function resizeMemoryCanvas() {
    const bounds = memoryCanvas.getBoundingClientRect();
    pixelRatio = Math.min(window.devicePixelRatio || 1, 2);
    canvasWidth = Math.max(1, Math.floor(bounds.width));
    canvasHeight = Math.max(1, Math.floor(bounds.height));
    memoryCanvas.width = Math.floor(canvasWidth * pixelRatio);
    memoryCanvas.height = Math.floor(canvasHeight * pixelRatio);
    context.setTransform(pixelRatio, 0, 0, pixelRatio, 0, 0);
    drawMemoryField(0);
  }

  function drawMemoryField(time) {
    context.clearRect(0, 0, canvasWidth, canvasHeight);
    context.fillStyle = "#0b1215";
    context.fillRect(0, 0, canvasWidth, canvasHeight);

    const fieldLeft = canvasWidth * 0.42;
    const coreX = canvasWidth * 0.78;
    const verticalStep = Math.max(42, canvasHeight / 12);
    const cycle = time / 1000;

    context.lineWidth = 1;
    context.strokeStyle = "rgba(143, 165, 169, 0.15)";
    for (let x = fieldLeft; x < canvasWidth; x += 64) {
      context.beginPath();
      context.moveTo(x + 0.5, 0);
      context.lineTo(x + 0.5, canvasHeight);
      context.stroke();
    }
    for (let y = verticalStep * 1.25; y < canvasHeight; y += verticalStep) {
      context.beginPath();
      context.moveTo(fieldLeft, y + 0.5);
      context.lineTo(canvasWidth, y + 0.5);
      context.stroke();
    }

    context.fillStyle = "rgba(35, 51, 57, 0.8)";
    context.fillRect(coreX - 14, canvasHeight * 0.13, 28, canvasHeight * 0.74);
    context.fillStyle = "rgba(42, 185, 130, 0.62)";
    context.fillRect(coreX - 1, canvasHeight * 0.13, 2, canvasHeight * 0.74);

    for (let index = 0; index < 10; index += 1) {
      const y = canvasHeight * 0.17 + index * verticalStep * 0.78;
      const startX = fieldLeft + 28 + (index % 3) * 28;
      const junctionX = coreX - 34;
      const color = palette[index % palette.length];
      const breathing = reducedMotion ? 0 : Math.sin(cycle * 1.4 + index * 0.7) * 8;
      const fragmentX = startX + ((cycle * (18 + index)) % 150);

      context.strokeStyle = `${color}88`;
      context.beginPath();
      context.moveTo(startX, y);
      context.lineTo(junctionX - 72, y);
      context.lineTo(junctionX, canvasHeight * 0.5 + breathing);
      context.lineTo(coreX - 16, canvasHeight * 0.5 + breathing);
      context.stroke();

      context.fillStyle = color;
      context.fillRect(fragmentX, y - 4, 8, 8);
      context.fillStyle = "rgba(238, 244, 243, 0.72)";
      context.fillRect(startX - 18, y - 3, 5, 5);
    }

    for (let index = 0; index < 3; index += 1) {
      const outputY = canvasHeight * 0.36 + index * verticalStep * 1.16;
      const color = palette[index === 1 ? 1 : 0];
      const pulse = reducedMotion ? 0 : Math.sin(cycle * 1.6 + index) * 4;
      context.strokeStyle = `${color}bb`;
      context.beginPath();
      context.moveTo(coreX + 16, canvasHeight * 0.5 + pulse);
      context.lineTo(coreX + 72, outputY);
      context.lineTo(canvasWidth - 54, outputY);
      context.stroke();
      context.fillStyle = color;
      context.fillRect(canvasWidth - 50, outputY - 5, 10, 10);
    }

    context.strokeStyle = "rgba(237, 244, 242, 0.3)";
    context.strokeRect(fieldLeft + 22.5, canvasHeight * 0.11 + 0.5, canvasWidth - fieldLeft - 66, canvasHeight * 0.78);
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
