import { createPinia } from 'pinia';
import { createApp } from 'vue';
import App from './App.vue';
import { router } from './appRouter.ts';
import i18n from './i18n.ts';
import { useCoreDataStore } from './stores';

const pinia = createPinia();
await useCoreDataStore(pinia).fetchData(); // fetch core data before mounting the app

const app = i18n(createApp(App));
app.use(pinia);
app.use(router);

app.directive('swap', (el, binding) => {
  if (el.parentNode) {
    el.outerHTML = binding.value;
  }
});

await router.isReady();
app.mount('#app');
