const DarkClass = "serenity-dark";

/**
 * Follows the system theme, which is what the WebView picks up from Windows.
 */
export function followSystemTheme(): void {
  const dark = window.matchMedia("(prefers-color-scheme: dark)");

  const apply = () => document.documentElement.classList.toggle(DarkClass, dark.matches);

  apply();
  dark.addEventListener("change", apply);
}
