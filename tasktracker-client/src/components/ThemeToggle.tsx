import { useTheme } from "../utils/theme";

type ThemeToggleProps = {
  className?: string;
  compact?: boolean;
};

export default function ThemeToggle({ className = "", compact = false }: ThemeToggleProps) {
  const { theme, toggleTheme } = useTheme();
  const isDark = theme === "dark";
  const nextLabel = isDark ? "Switch to light theme" : "Switch to dark theme";

  return (
    <button
      type="button"
      className={`theme-toggle${compact ? " theme-toggle--compact" : ""}${className ? ` ${className}` : ""}`}
      onClick={toggleTheme}
      aria-label={nextLabel}
      title={nextLabel}
      data-theme-current={theme}
    >
      {isDark ? (
        <svg className="navigation-icon" aria-hidden="true" viewBox="0 0 24 24">
          <circle cx="12" cy="12" r="4" />
          <path d="M12 3v2M12 19v2M4.93 4.93l1.41 1.41M17.66 17.66l1.41 1.41M3 12h2M19 12h2M4.93 19.07l1.41-1.41M17.66 6.34l1.41-1.41" />
        </svg>
      ) : (
        <svg className="navigation-icon" aria-hidden="true" viewBox="0 0 24 24">
          <path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z" />
        </svg>
      )}
      {!compact && <span>{isDark ? "Light mode" : "Dark mode"}</span>}
    </button>
  );
}
