function getSafeLoginRedirect(value: string | null): string | null {
  if (
    !value ||
    !value.startsWith("/") ||
    value.startsWith("//") ||
    value.includes("\\") ||
    Array.from(value).some((character) =>
      character.charCodeAt(0) < 32 || character.charCodeAt(0) === 127
    )
  ) {
    return null;
  }

  try {
    const baseUrl = "https://tasktracker.invalid";
    const url = new URL(value, baseUrl);
    const pathname = decodeURIComponent(url.pathname);

    if (
      url.origin !== baseUrl ||
      pathname.startsWith("//") ||
      pathname.includes("\\") ||
      /^\/login\/*$/i.test(pathname)
    ) {
      return null;
    }

    return `${url.pathname}${url.search}${url.hash}`;
  } catch {
    return null;
  }
}

export { getSafeLoginRedirect };
