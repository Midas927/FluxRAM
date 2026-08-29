const year = document.getElementById("current-year");
if (year) year.textContent = String(new Date().getFullYear());

const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
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
