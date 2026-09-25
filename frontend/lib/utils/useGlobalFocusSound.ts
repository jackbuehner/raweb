import { onMounted, onUnmounted } from 'vue';
import { ElementSoundKind, ElementSoundPlayer } from './ElementSoundPlayer.ts';

/**
 * Plays a sound when keyboard/controller focus moves between elements, but not
 * when focus lands on an element because it was clicked/tapped (mirrors WinUI's
 * automatic ElementSoundKind.Focus on focus changes). Each Vue app root (the
 * main app and the docs/wiki app are separate apps) must call this once.
 */
export function useGlobalFocusSound() {
  // Browsers treat text-entry controls (<input type="text">, <textarea>, etc.)
  // as a special case where :focus-visible matches even when focused by a
  // mouse/touch click (so the focus ring still shows for them). That means
  // :focus-visible alone can't distinguish pointer-triggered focus from
  // keyboard-triggered focus for those elements, so track pointer activity
  // ourselves and treat any focus that immediately follows a pointer press as
  // pointer-triggered, regardless of what :focus-visible says.
  let focusedViaPointer = false;
  function handlePointerDown() {
    focusedViaPointer = true;
  }
  // any keyboard activity means the user is back to using the keyboard, which
  // clears a stale pointer flag left over from a click that didn't move focus
  // (e.g. clicking empty space, then tabbing)
  function handleKeyDown() {
    focusedViaPointer = false;
  }

  let lastFocusTarget: EventTarget | null = null;
  function handleFocusIn(event: FocusEvent) {
    // always drain a pending suppression, even if the deduplication check
    // below skips playing anything, so it never leaks into a later,
    // unrelated Focus sound
    const suppressed = ElementSoundPlayer.wasSuppressed(ElementSoundKind.Focus);
    const viaPointer = focusedViaPointer;
    focusedViaPointer = false;

    if (event.target === lastFocusTarget) return;
    lastFocusTarget = event.target;
    if (suppressed || viaPointer) return;
    if (
      event.target instanceof HTMLElement &&
      event.target !== document.body &&
      event.target.matches(':focus-visible')
    ) {
      ElementSoundPlayer.play(ElementSoundKind.Focus);
    }
  }

  onMounted(() => {
    // capture phase so these run before the focusin they precede
    document.addEventListener('pointerdown', handlePointerDown, true);
    document.addEventListener('keydown', handleKeyDown, true);
    document.addEventListener('focusin', handleFocusIn);
  });
  onUnmounted(() => {
    document.removeEventListener('pointerdown', handlePointerDown, true);
    document.removeEventListener('keydown', handleKeyDown, true);
    document.removeEventListener('focusin', handleFocusIn);
  });
}
