import { createWritableBooleanSetting } from './createBooleanWritableSetting';

export const soundEffectsEnabled = createWritableBooleanSetting('sound-effects:enabled', 'soundEffectsEnabled', true);
