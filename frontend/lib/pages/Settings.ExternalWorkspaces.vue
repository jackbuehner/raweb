<script setup lang="ts">
  import { Button, ContentDialog, Field, InfoBar, TextBlock, TextBox } from '$components';
  import { showConfirm } from '$dialogs';
  import { isUrl, workspaceTokens } from '$utils';
  import { useTranslation } from 'i18next-vue';
  import { onMounted, ref } from 'vue';

  const { t } = useTranslation();
  const props = defineProps<import('./types.d.ts').PageProps>();

  const workspaces = ref<{ endpoint: string; name: string }[]>([]);
  const unlocking = ref(false);
  const unlockError = ref<Error | null>(null);

  function refreshWorkspaces() {
    workspaces.value = workspaceTokens.list();
    props.refreshWorkspace();
  }

  async function unlock() {
    if (workspaceTokens.isInitialized) {
      refreshWorkspaces();
      return true;
    }

    unlocking.value = true;
    unlockError.value = null;

    try {
      await workspaceTokens.decryptPersistedTokens();
      refreshWorkspaces();
      return true;
    } catch (error) {
      if (error instanceof Error) {
        unlockError.value = error;
      } else if (error) {
        unlockError.value = new Error(String(error));
      }
      return false;
    } finally {
      unlocking.value = false;
    }
  }

  onMounted(unlock);

  const newWorkspaceName = ref('');
  const newWorkspaceUrl = ref('');
  const newWorkspaceUsername = ref('');
  const newWorkspacePassword = ref('');
  const addError = ref<string | null>(null);

  function resetAddForm() {
    newWorkspaceName.value = '';
    newWorkspaceUrl.value = '';
    newWorkspaceUsername.value = '';
    newWorkspacePassword.value = '';
    addError.value = null;
  }

  async function submitAddWorkspace(close: () => void) {
    const name = newWorkspaceName.value.trim();
    const endpoint = newWorkspaceUrl.value.trim();
    const username = newWorkspaceUsername.value.trim();
    const password = newWorkspacePassword.value;

    if (!name || !isUrl(endpoint, { requireTopLevelDomain: true })) {
      addError.value = t('settings.externalWorkspaces.manager.addDialog.invalidUrl');
      return;
    }
    if (!username || !password) {
      addError.value = t('settings.externalWorkspaces.manager.addDialog.credentialsRequired');
      return;
    }

    if (!(await unlock())) {
      return;
    }

    workspaceTokens.registerWorkspace(endpoint, name, username, password);
    refreshWorkspaces();
    close();
    resetAddForm();
  }

  const loadingConnectionFile = ref<string | null>(null);
  const loadingCopyUrl = ref<string | null>(null);

  function copyWorkspaceUrl(endpoint: string) {
    loadingCopyUrl.value = endpoint;
    setTimeout(() => {
      if (loadingCopyUrl.value === endpoint) {
        loadingCopyUrl.value = null;
      }
    }, 250);

    navigator.clipboard.writeText(endpoint).catch((err) => {
      console.error('Failed to copy workspace URL: ', err);
    });
  }

  async function confirmRemoveWorkspace(endpoint: string, name: string) {
    await showConfirm(
      t('settings.externalWorkspaces.manager.removeConfirm.title'),
      t('settings.externalWorkspaces.manager.removeConfirm.message', { name }),
      t('settings.externalWorkspaces.manager.removeConfirm.confirm'),
      t('dialog.cancel')
    )
      .then((done) => {
        workspaceTokens.unregisterWorkspace(endpoint);
        refreshWorkspaces();
        done();
      })
      .catch(() => {});
  }
</script>

<template>
  <div class="titlebar-row">
    <RouterLink to="/settings/" custom v-slot="{ href, navigate }">
      <a :href="href" @click="navigate" class="breadcrumb-link">
        <TextBlock variant="title">{{ t('settings.title') }}</TextBlock>
      </a>
    </RouterLink>
    <TextBlock variant="title" style="margin: 0 1rem"> › </TextBlock>
    <TextBlock variant="title">{{ t('settings.externalWorkspaces.manager.title') }}</TextBlock>
  </div>

  <div v-if="!workspaceTokens.isInitialized" class="full-page-notice">
    <TextBlock variant="subtitle">{{ t('settings.externalWorkspaces.manager.lockedTitle') }}</TextBlock>
    <InfoBar v-if="unlockError" severity="critical">
      <TextBlock>{{ unlockError.message }}</TextBlock>
    </InfoBar>
    <TextBlock block>{{ t('settings.externalWorkspaces.manager.locked') }}</TextBlock>
    <div class="button-row">
      <Button variant="accent" @click="unlock" :loading="unlocking">
        {{ t('settings.externalWorkspaces.manager.unlock') }}
      </Button>
    </div>
  </div>

  <template v-else>
    <div class="header-actions">
      <ContentDialog :title="t('settings.externalWorkspaces.manager.addDialog.title')" @close="resetAddForm">
        <template #opener="{ open }">
          <Button @click="open">{{ t('settings.externalWorkspaces.manager.add') }}</Button>
        </template>

        <template #default>
          <form class="add-workspace-form" @submit.prevent>
            <Field>
              <TextBlock>{{ t('settings.externalWorkspaces.manager.addDialog.name') }}</TextBlock>
              <TextBox
                v-model:value="newWorkspaceName"
                :placeholder="t('settings.externalWorkspaces.manager.addDialog.namePlaceholder')"
              />
            </Field>
            <Field>
              <TextBlock>{{ t('settings.externalWorkspaces.manager.addDialog.url') }}</TextBlock>
              <TextBox
                v-model:value="newWorkspaceUrl"
                type="url"
                :placeholder="t('settings.externalWorkspaces.manager.addDialog.urlPlaceholder')"
              />
            </Field>
            <Field>
              <TextBlock>{{ t('settings.externalWorkspaces.manager.addDialog.username') }}</TextBlock>
              <TextBox
                v-model:value="newWorkspaceUsername"
                autocomplete="username"
                :placeholder="t('settings.externalWorkspaces.manager.addDialog.usernamePlaceholder')"
              />
            </Field>
            <Field>
              <TextBlock>{{ t('settings.externalWorkspaces.manager.addDialog.password') }}</TextBlock>
              <TextBox v-model:value="newWorkspacePassword" type="password" autocomplete="current-password" />
            </Field>
            <TextBlock v-if="addError" style="color: var(--wui-text-error)">{{ addError }}</TextBlock>
          </form>
        </template>

        <template #footer="{ close }">
          <Button variant="accent" @click="submitAddWorkspace(close)">{{
            t('settings.externalWorkspaces.manager.addDialog.save')
          }}</Button>
          <Button @click="close">{{ t('dialog.cancel') }}</Button>
        </template>
      </ContentDialog>
    </div>

    <TextBlock v-if="workspaces.length === 0" class="empty-state">
      {{ t('settings.externalWorkspaces.manager.empty') }}
    </TextBlock>

    <div class="workspaces-list" v-else>
      <div class="workspace" v-for="workspace in workspaces" :key="workspace.endpoint">
        <div class="workspace-info">
          <TextBlock variant="bodyStrong">{{ workspace.name }}</TextBlock>
          <TextBlock variant="body">{{ workspace.endpoint }}</TextBlock>
        </div>
        <div class="button-row">
          <Button
            @click="copyWorkspaceUrl(workspace.endpoint)"
            :loading="loadingCopyUrl === workspace.endpoint"
          >
            {{ t('settings.externalWorkspaces.manager.copy') }}
          </Button>
          <Button @click="confirmRemoveWorkspace(workspace.endpoint, workspace.name)">
            {{ t('settings.externalWorkspaces.manager.remove') }}
          </Button>
        </div>
      </div>
    </div>
  </template>
</template>

<style scoped>
  .titlebar-row {
    user-select: none;
    margin-bottom: 16px;
  }

  .header-actions {
    margin: 0 0 16px 0;
  }

  .add-workspace-form {
    display: flex;
    flex-direction: column;
    min-width: 20rem;
  }

  .empty-state {
    opacity: 0.7;
  }

  .full-page-notice {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    block-size: calc(100% - 52px);
    gap: 8px;
    padding: 24px 16px;
    background-color: var(--wui-subtle-transparent);
    border-radius: var(--wui-control-corner-radius);
    box-sizing: border-box;
    text-align: center;
  }
  .full-page-notice .button-row {
    display: flex;
    flex-direction: row;
    gap: 8px;
    margin-top: 12px;
  }

  .workspaces-list {
    display: flex;
    flex-direction: column;
    gap: 12px;
  }

  .workspace {
    background-color: var(--wui-card-background-default);
    border: 1px solid var(--wui-card-stroke-default);
    border-radius: var(--wui-overlay-corner-radius);
    padding: 16px;
  }

  .workspace-info {
    display: flex;
    flex-direction: column;
    gap: 2px;
    margin-bottom: 12px;
    word-break: break-all;
  }

  .button-row {
    display: flex;
    flex-direction: row;
    flex-wrap: wrap;
    gap: 8px;
  }

  .breadcrumb-link {
    text-decoration: none;
    cursor: default;
    color: var(--wui-text-secondary);
  }
  .breadcrumb-link:hover {
    color: var(--wui-text-primary);
  }
  .breadcrumb-link:active {
    color: var(--wui-text-tertiary);
  }
</style>
