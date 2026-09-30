const DarkClass = "serenity-dark";

/**
 * Sigue el tema del sistema, que es lo que trae el WebView desde Windows.
 */
export function followSystemTheme(): void {
  const dark = window.matchMedia("(prefers-color-scheme: dark)");

  const apply = () => document.documentElement.classList.toggle(DarkClass, dark.matches);

  apply();
  dark.addEventListener("change", apply);
}
