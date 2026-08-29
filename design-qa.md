# FluxRAM Website Background QA

## Comparison Target

- Background source visual: `.design/fluxram-site-redesign/references/memory-grid-background-reference.png`.
- Overall layout source: `.design/fluxram-site-redesign/options/option-2.png` and the existing redesigned website.
- Final desktop hero: `.design/fluxram-site-redesign/screenshots/hybrid-memory-grid-desktop.png`.
- Final mobile hero: `.design/fluxram-site-redesign/screenshots/hybrid-memory-grid-mobile.png`.
- Lower-page evidence: `.design/fluxram-site-redesign/screenshots/hybrid-memory-grid-desktop-middle.png` and `.design/fluxram-site-redesign/screenshots/hybrid-memory-grid-desktop-lower.png`.
- Focused background comparison: `.design/fluxram-site-redesign/screenshots/qa-memory-grid-background-comparison.png`.
- Source image: 2538 x 1150 px. It was normalized to 1269 x 575 px, then compared with an equal 700 x 260 px background-only crop from the browser implementation.
- Desktop implementation: 1425 x 1013 px at a 1440 x 1024 CSS viewport.
- Mobile implementation: 360 x 779 px at a 375 x 812 CSS viewport.
- State: page top, normal motion, no menu or FAQ open.

## Scope Boundary

The user's reference applies only to the animated hero background. Foreground typography, copy, actions, facts, navigation, proof strip, and every lower-page section intentionally remain from the redesigned website.

## State And Interaction Evidence

- `#hero-memory-canvas[data-visual="memory-grid"]` fills the complete hero on desktop and mobile.
- Desktop Canvas size is 1425 x 664 CSS pixels; mobile Canvas size is 360 x 577 CSS pixels.
- Mobile removes the old reserved visual stage, so the full-background treatment does not create empty space.
- Mobile menu opens with six links and closes normally.
- FAQ entry “能直接提升 FPS 吗？” opens its answer, with only one FAQ open.
- GitCode Portable remains the primary action; GitHub Portable remains the secondary action.
- Browser console: no warnings or errors.
- Desktop and mobile: no horizontal overflow.

## Full-View Evidence

The accepted top, middle, and lower viewport captures show that only the hero background changed. The editorial process ledger, action comparison, safety ledger, edition table, download choices, FAQ, and footer retain the selected redesign's layout and styling.

## Focused Background Comparison

The side-by-side crop compares only background regions. Both use a blue-black base, dense 30 x 19 pixel memory blocks, six-pixel gaps, sparse emerald protected blocks, quieter steel/amber cells, and faint horizontal/vertical guides. The implementation is live Canvas rather than a raster wallpaper, preserving the source atmosphere while remaining responsive.

## Required Fidelity Surfaces

- Fonts and typography: unchanged from the redesigned site; Chinese headings, UI labels, and body copy remain readable at desktop and mobile widths.
- Spacing and layout rhythm: redesigned hero grid and lower-page spacing remain unchanged; only the mobile-only empty visual column was removed because the background now fills the hero.
- Colors and visual tokens: the reference's blue-black, low-contrast green, muted steel, and amber background palette is reproduced without changing foreground action colors.
- Image quality and asset fidelity: the supplied screenshot is retained as design evidence only. Production uses a deterministic, DPR-aware Canvas memory grid, not a stretched screenshot, CSS drawing, or generated poster.
- Copy and content: version, download URLs, product boundaries, and Pro distinction remain intact. The “未知发布者” FAQ was removed by explicit user request before publication.

## Findings And Fixes

1. P2 fixed: the first Canvas pass used 38 x 23 pixel desktop blocks and looked too large and sparse. It now uses 30 x 19 pixel blocks with six-pixel gaps, matching the normalized reference.
2. P2 fixed: the old mobile signal visualization reserved 300 pixels below the copy. The new full-background Canvas removes that stage on mobile, eliminating the empty area.
3. Verification complete: responsive screenshots, focused source comparison, mobile menu, FAQ, download DOM, console logs, JavaScript syntax, .NET tests, and overflow checks passed.

## Residual Notes

- Foreground layout differs from the reference screenshot by explicit user request.
- Canvas cells animate subtly and pause outside the hero or when the tab is hidden; reduced-motion users receive a static rendered field.

## Final Result

final result: passed
