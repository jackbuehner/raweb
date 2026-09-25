<script setup lang="ts">
  import { AnimatedNavigationItemIndicator, Button } from '$components';
  import { ElementSoundKind, ElementSoundPlayer } from '$utils';

  const {
    selected = false,
    disabled = false,
    href,
    indicatorSize = 16,
  } = defineProps<{
    /** Whether this item is the current selection. */
    selected?: boolean;
    disabled?: boolean;
    /** Renders the item as a link (e.g. for use with a RouterLink). */
    href?: string;
    /** Length of the accent underline indicator. Defaults to a short, centered bar. */
    indicatorSize?: number;
  }>();

  const emit = defineEmits<{
    (e: 'click', event: MouseEvent): void;
  }>();

  // play a directional move sound based on this button's position relative to
  // the currently selected one, instead of the button's default invoke sound
  function handleClick(event: MouseEvent) {
    const button = event.currentTarget as HTMLElement;
    const bar = button.closest('.selector-bar');
    const siblings = bar ? Array.from(bar.querySelectorAll<HTMLElement>('.selector-bar-button')) : [];
    const currentIndex = siblings.findIndex((el) => el.classList.contains('selected'));
    const targetIndex = siblings.indexOf(button);

    if (currentIndex !== -1 && targetIndex !== -1 && targetIndex !== currentIndex) {
      ElementSoundPlayer.play(targetIndex > currentIndex ? ElementSoundKind.MoveNext : ElementSoundKind.MovePrevious);
    } else {
      ElementSoundPlayer.play(ElementSoundKind.Invoke);
    }

    emit('click', event);
  }
</script>

<template>
  <AnimatedNavigationItemIndicator.Selectable :selected :indicator-size="indicatorSize">
    <Button
      :href
      :disabled
      role="tab"
      :aria-selected="selected"
      class="selector-bar-button"
      :class="{ selected }"
      sound="none"
      @click="handleClick"
    >
      <template v-if="$slots.icon" #icon><slot name="icon"></slot></template>
      <slot></slot>
    </Button>
  </AnimatedNavigationItemIndicator.Selectable>
</template>

<style scoped>
  .selector-bar-button {
    --wui-control-fill-default: transparent;
    box-shadow: none !important;
    height: 2.5rem;
    -webkit-user-drag: none;
    transition: color var(--wui-control-normal-duration);
  }
  .selector-bar-button:not(.disabled):hover {
    background-color: var(--wui-subtle-secondary);
    color: var(--wui-text-primary);
  }
  .selector-bar-button:not(.disabled):active {
    background-color: var(--wui-subtle-tertiary);
    color: var(--wui-text-tertiary);
  }
  .selector-bar-button.selected {
    background-color: transparent !important;
  }
</style>
