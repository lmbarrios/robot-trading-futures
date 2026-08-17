---
name: Cyber-Tactical HUD
colors:
  surface: '#051424'
  surface-dim: '#051424'
  surface-bright: '#2c3a4c'
  surface-container-lowest: '#010f1f'
  surface-container-low: '#0d1c2d'
  surface-container: '#122131'
  surface-container-high: '#1c2b3c'
  surface-container-highest: '#273647'
  on-surface: '#d4e4fa'
  on-surface-variant: '#c6c6cd'
  inverse-surface: '#d4e4fa'
  inverse-on-surface: '#233143'
  outline: '#909097'
  outline-variant: '#45464d'
  surface-tint: '#bec6e0'
  primary: '#bec6e0'
  on-primary: '#283044'
  primary-container: '#0f172a'
  on-primary-container: '#798098'
  inverse-primary: '#565e74'
  secondary: '#93ccff'
  on-secondary: '#003351'
  secondary-container: '#3198dc'
  on-secondary-container: '#002c47'
  tertiary: '#4edea3'
  on-tertiary: '#003824'
  tertiary-container: '#001c10'
  on-tertiary-container: '#009365'
  error: '#ffb4ab'
  on-error: '#690005'
  error-container: '#93000a'
  on-error-container: '#ffdad6'
  primary-fixed: '#dae2fd'
  primary-fixed-dim: '#bec6e0'
  on-primary-fixed: '#131b2e'
  on-primary-fixed-variant: '#3f465c'
  secondary-fixed: '#cce5ff'
  secondary-fixed-dim: '#93ccff'
  on-secondary-fixed: '#001d31'
  on-secondary-fixed-variant: '#004b73'
  tertiary-fixed: '#6ffbbe'
  tertiary-fixed-dim: '#4edea3'
  on-tertiary-fixed: '#002113'
  on-tertiary-fixed-variant: '#005236'
  background: '#051424'
  on-background: '#d4e4fa'
  surface-variant: '#273647'
typography:
  headline-lg:
    fontFamily: Geist
    fontSize: 32px
    fontWeight: '700'
    lineHeight: 40px
    letterSpacing: -0.02em
  headline-md:
    fontFamily: Geist
    fontSize: 24px
    fontWeight: '600'
    lineHeight: 32px
  body-lg:
    fontFamily: Inter
    fontSize: 16px
    fontWeight: '400'
    lineHeight: 24px
  body-md:
    fontFamily: Inter
    fontSize: 14px
    fontWeight: '400'
    lineHeight: 20px
  data-lg:
    fontFamily: JetBrains Mono
    fontSize: 18px
    fontWeight: '600'
    lineHeight: 24px
  data-md:
    fontFamily: JetBrains Mono
    fontSize: 14px
    fontWeight: '500'
    lineHeight: 18px
  label-sm:
    fontFamily: JetBrains Mono
    fontSize: 11px
    fontWeight: '700'
    lineHeight: 16px
rounded:
  sm: 0.125rem
  DEFAULT: 0.25rem
  md: 0.375rem
  lg: 0.5rem
  xl: 0.75rem
  full: 9999px
spacing:
  unit: 4px
  gutter: 16px
  margin: 24px
  container-padding: 12px
---

## Brand & Style
The design system is a high-performance, futuristic trading interface designed for NinjaTrader 8. It prioritizes rapid information processing and mission-critical clarity through a "Tactical HUD" (Heads-Up Display) aesthetic.

The visual style combines **Glassmorphism** with **High-Contrast Neon** accents. It leverages semi-transparent surfaces to maintain a sense of depth and spatial awareness, while using vibrant glowing edges to define boundaries without heavy visual weight. The emotional response is one of precision, technological sophistication, and calm under high-pressure market conditions.

## Colors
The palette is rooted in a "Deep Slate" foundation to minimize eye strain during extended trading sessions. 

- **Primary (#0F172A):** The void. Used for the main application background.
- **Accent Cyan (#0284C7):** The "Life-Line." Used for active borders, focus states, and primary navigation elements.
- **Success Emerald (#10B981):** Used for "Long" positions, profit indicators, and system-ready states.
- **Panic Red (#EF4444):** Reserved for "Short" positions, losses, and critical system failures.
- **Alert Orange (#F59E0B):** Used for pending orders, warnings, and manual control overrides.
- **Glass Fill:** Surfaces use a 60% opacity version of the primary color with a backdrop blur of 12px-20px.

## Typography
The typography system uses three distinct typefaces to separate intent:
1. **Geist** for structural headings and primary UI labels, providing a modern, technical feel.
2. **Inter** for general interface text and descriptive content to ensure maximum readability.
3. **JetBrains Mono** for all numerical data, price feeds, and timestamps. The monospaced nature prevents "jumping" numbers during high-volatility price action.

All labels should be rendered with a slight tracking increase (0.05em) to enhance legibility on dark backgrounds.

## Layout & Spacing
The layout follows a strict 4px grid system to maintain a "technical" alignment. 

- **Grid Model:** 12-column fluid grid for dashboard views, with modular "pods" that can be rearranged.
- **Density:** High-density layout. Padding is minimized in data tables to maximize the visibility of multiple timeframes and tickers simultaneously.
- **Breakpoints:**
  - **Mobile/Compact:** 4 columns, sidebar hidden.
  - **Standard Desktop:** 12 columns, fixed left-hand navigation.
  - **Ultra-Wide:** 16 columns for multi-chart orchestration.

## Elevation & Depth
Depth is created through **Layered Glassmorphism** rather than traditional shadows.

1. **Base Layer:** Deep Slate (#0F172A) solid.
2. **Surface Layer (Cards):** Primary color at 60% opacity + 1px interior border (White @ 10% opacity) + 16px backdrop blur.
3. **Active/Focus Layer:** Same as Surface, but with a 1px exterior "Neon Glow" border (#0284C7).
4. **Glow Effects:** Use `box-shadow: 0 0 8px 0px [Color]` for active LED indicators and primary action buttons to simulate light emission.

## Shapes
This design system utilizes a "Soft" geometric language (4px radius). This creates a professional, engineered look that feels modern without the playfulness of fully rounded corners. 

- **Buttons & Inputs:** 4px radius.
- **Status LEDs:** Circular (50% radius) to differentiate from interactive elements.
- **Data Tags:** 2px radius for a sharper, more precise appearance in dense tables.

## Components

### Buttons
- **Primary:** Neon Cyan background, black text. High-contrast. 4px glow on hover.
- **Ghost/Tactical:** No background, 1px Cyan border. Used for secondary controls.
- **Buy/Long:** Emerald Green background with white text.
- **Sell/Short:** Panic Red background with white text.

### Glass Cards
Every container must use the backdrop-filter: blur(16px). Borders are 1px thick. For inactive containers, use a neutral slate border. For the "Current Active Symbol," use the Neon Cyan border.

### Status Indicators (LEDs)
Small 8px circles with a heavy outer glow. 
- **Connected:** Pulsing Emerald Green.
- **Disconnected:** Solid Panic Red.
- **Syncing:** Rotating Alert Orange.

### Input Fields
Darker than the card surface (Primary @ 80% opacity), 1px slate border that turns Cyan on focus. Text is always monospaced for numerical inputs.

### Data Tables
No vertical lines. Horizontal lines are 1px solid at 5% white opacity. Row highlighting on hover uses a 10% white overlay.