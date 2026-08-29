# FluxRAM Website Redesign QA

## Comparison Target

- Source visual truth: `.design/fluxram-site-redesign/options/option-2.png`
- Desktop implementation: `.design/fluxram-site-redesign/screenshots/implementation-desktop-1440-final.png`
- Mobile implementation: `.design/fluxram-site-redesign/screenshots/implementation-mobile-375-final.png`
- Combined hero evidence: `.design/fluxram-site-redesign/screenshots/qa-hero-comparison.png`
- Source dimensions: 899 x 1750 px.
- Desktop capture: 1425 x 1013 px at a 1440 x 1024 CSS viewport.
- Mobile capture: 360 x 779 px at a 375 x 812 CSS viewport.

## State And Interaction Evidence

- Hero at scroll position 0.
- Mobile menu opens and exposes navigation.
- FAQ question "能直接提升 FPS 吗？" opens its answer.
- Primary GitCode Portable link, GitHub fallback links, and SHA256 links are present in the rendered DOM.
- Browser console: no warnings or errors.

## Full View Comparison

The implementation preserves the selected performance-field-manual direction: mineral paper background, graphite evidence bands, precise emerald action color, numbered sections, editorial hierarchy, and a product-specific process-family visual. The coded page intentionally uses FluxRAM's real download and product copy rather than the generated comp's illustrative placeholder text.

## Focused Hero Comparison

The paired screenshot compares an equal-height top crop of the selected visual with the actual desktop hero. The source foregrounds the FluxRAM product name, explanatory process diagram, and domestic download. The implementation now matches all three priorities: FluxRAM 0.4 appears in the hero, the process-family visual is a real generated asset, and the GitCode domestic download remains the primary action.

## Required Fidelity Surfaces

- Fonts and typography: Chinese system sans stack uses a clear display/body hierarchy; technical metadata uses monospace fallback only where meaningful. Desktop and mobile title wrapping was checked and adjusted.
- Spacing and layout rhythm: hero, proof strip, numbered sections, comparison, downloads, and FAQ use a fixed spacing scale. Mobile changes to single-column reading rather than shrinking desktop grids.
- Colors and visual tokens: paper, ink, emerald, cobalt, orange, and line tokens are recorded in `.tastemaker/style-lock.md`. The contrast contract was checked before implementation.
- Image quality and asset fidelity: `site/assets/memory-field-manual.png` is a project-bound generated raster asset, used in the hero with descriptive alt text. The existing FluxRAM icon is preserved unchanged.
- Copy and content: product claims reflect the current 0.4 implementation. No testimonials, benchmark numbers, or customer logos were fabricated.

## Comparison History

1. P2 fixed: desktop hero title wrapped too aggressively. The desktop grid was rebalanced and title scale reduced.
2. P2 fixed: mobile title created an orphaned final character. The mobile base type scale was reduced to a readable three-line layout.
3. P2 fixed: tagline activation was too late on narrow screens. Trigger threshold was lowered and its muted state strengthened for readability.
4. P2 fixed: selected visual foregrounded FluxRAM more strongly than the initial implementation. `FluxRAM 0.4` was promoted into the hero hierarchy.

## Residual Notes

- The generated reference contains illustrative process labels that are not copied verbatim. Real product copy and direct download links take precedence.
- The page intentionally does not use a manual dark-mode toggle because the selected direction is a fixed light technical manual with dark evidence bands.

## Final Result

final result: passed
