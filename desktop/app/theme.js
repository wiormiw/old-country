// Applies the saved colour theme before the app has loaded, so it doesn't flash the other one.
// The app keeps this cache up to date (see ThemeChoice in src/state.rs).
try {
  const theme = localStorage.getItem("old-country.theme");
  if (theme === "light" || theme === "dark") document.documentElement.dataset.theme = theme;
} catch {}
