import { useLayoutEffect, type RefObject } from "react";

export function useModalFocus(dialog: RefObject<HTMLDialogElement | null>, opener?: RefObject<HTMLElement | null>,
  fallback?: RefObject<HTMLElement | null>) {
  useLayoutEffect(() => {
    const modal = dialog.current;
    if (!modal) return;
    const fallbackTarget = fallback?.current;
    const priorFocus = opener?.current ?? fallbackTarget ?? document.activeElement;
    modal.showModal();
    return () => {
      modal.close();
      if (document.activeElement === document.body || document.activeElement === priorFocus) {
        const target = priorFocus?.isConnected ? priorFocus : fallbackTarget;
        if (target instanceof HTMLElement && target.isConnected) target.focus({ preventScroll: true });
      }
    };
  }, [dialog, opener, fallback]);
}
