/** @type {import('tailwindcss').Config} */
module.exports = {
  darkMode: "class",
  content: [
    "./Views/**/*.cshtml",
    "./wwwroot/**/*.html",
    "./wwwroot/js/**/*.js"
  ],
  safelist: [
    // Dynamically-built classnames (interpolated in Razor / JS) that the
    // static content scan can't see because they never appear literally.
    { pattern: /^ui-(btn|card|badge|alert|modal|loader|progress|check|switch|radio|field|label|tab|stat|table|section|skeleton|required)/ }
  ],
  theme: {
    extend: {
      fontFamily: {
        sans: ["Inter", "system-ui", "sans-serif"],
        mono: ["JetBrains Mono", "monospace"]
      },
      colors: {
        surface: "var(--vw-surface)",
        canvas: "var(--vw-bg)",
        accent: "var(--vw-accent)",
        secondary: "var(--vw-secondary)"
      },
      keyframes: {
        vwFadeIn: { from: { opacity: 0 }, to: { opacity: 1 } },
        vwModalIn: {
          from: { opacity: 0, transform: "translateY(24px) scale(.98)" },
          to: { opacity: 1, transform: "translateY(0) scale(1)" }
        }
      },
      animation: {
        "fade-in": "vwFadeIn .15s ease-out",
        "modal-in": "vwModalIn .22s cubic-bezier(.16,1,.3,1)"
      }
    }
  },
  plugins: []
};
