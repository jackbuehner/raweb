import { useCoreDataStore } from '$stores';
import { isBrowser } from './environment.ts';
import { soundEffectsEnabled } from './soundEffects.ts';

export enum ElementSoundKind {
  Focus = 'Focus',
  Invoke = 'Invoke',
  Show = 'Show',
  Hide = 'Hide',
  MovePrevious = 'MovePrevious',
  MoveNext = 'MoveNext',
  GoBack = 'GoBack',
}

export enum ElementSoundPlayerState {
  /** Sounds play or not depending on the user's sound effects setting. */
  Auto = 'Auto',
  /** Sounds always play, regardless of the user's sound effects setting. */
  On = 'On',
  /** Sounds never play, regardless of the user's sound effects setting. */
  Off = 'Off',
}

// Focus has several interchangeable takes so the sound does not feel
// repetitive when it plays very frequently (e.g. tabbing through a list).
const soundFiles: Record<ElementSoundKind, string[]> = {
  [ElementSoundKind.Focus]: ['Focus1.flac', 'Focus2.flac', 'Focus3.flac', 'Focus4.flac', 'Focus5.flac'],
  [ElementSoundKind.Invoke]: ['Invoke.flac'],
  [ElementSoundKind.Show]: ['Show.flac'],
  [ElementSoundKind.Hide]: ['Hide.flac'],
  [ElementSoundKind.MovePrevious]: ['MovePrevious.flac'],
  [ElementSoundKind.MoveNext]: ['MoveNext.flac'],
  [ElementSoundKind.GoBack]: ['GoBack.flac'],
};

let state: ElementSoundPlayerState = ElementSoundPlayerState.Auto;

// only one sound is ever audible at a time; starting a new one cancels
// whichever sound is still playing so the most recently requested sound wins
let activeAudio: HTMLAudioElement | null = null;

// Some sounds are triggered from browser-native events (focusin, popover
// 'toggle') rather than from our own code directly, and those native events
// are not guaranteed to fire synchronously/in a predictable order relative to
// other code (e.g. a popover's 'toggle' event is queued, not synchronous).
// That makes call-order tricks unreliable for suppressing one sound in favor
// of another. Instead, code that is about to trigger one of these native
// events as a side effect of an action that already plays its own sound can
// call suppressNext(kind) beforehand; whenever the native-event handler
// eventually runs (regardless of timing), it consumes the flag and skips
// that one sound.
const suppressedKinds = new Set<ElementSoundKind>();

export const ElementSoundPlayer = {
  get State(): ElementSoundPlayerState {
    return state;
  },
  set State(value: ElementSoundPlayerState) {
    state = value;
  },

  /** Skips the next play() of this kind, whenever it happens. See note above. */
  suppressNext(kind: ElementSoundKind) {
    suppressedKinds.add(kind);
  },

  /**
   * Consumes a pending suppressNext(kind), if any, and reports whether one was
   * pending. Useful when a native-event handler has a code path that would
   * otherwise skip calling play(kind) entirely (e.g. a de-duplication check),
   * to avoid leaving a stale suppression that silences some later, unrelated
   * sound of the same kind.
   */
  wasSuppressed(kind: ElementSoundKind): boolean {
    return suppressedKinds.delete(kind);
  },

  play(kind: ElementSoundKind) {
    if (this.wasSuppressed(kind)) return;
    if (!isBrowser) return;
    if (state === ElementSoundPlayerState.Off) return;
    if (state === ElementSoundPlayerState.Auto && !soundEffectsEnabled.value) return;

    if (activeAudio) {
      activeAudio.pause();
      activeAudio = null;
    }

    const files = soundFiles[kind];
    const file = files[Math.floor(Math.random() * files.length)];
    const { appBase } = useCoreDataStore();

    const audio = new Audio(`${appBase}lib/assets/sounds/${file}`);
    activeAudio = audio;
    audio.addEventListener('ended', () => {
      if (activeAudio === audio) activeAudio = null;
    });
    audio.play().catch(() => {
      if (activeAudio === audio) activeAudio = null;
    });
  },
};
