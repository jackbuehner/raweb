import { useCoreDataStore } from '$stores';
import { prefixUserNS } from '$utils';
import { isBrowser } from '$utils/environment.ts';
import { parse, stringify } from 'devalue';
import { computed, ComputedRef, ref, WritableComputedRef } from 'vue';
import { getAppsAndDevices, getExternalWorkspaceAppsAndDevices } from './getAppsAndDevices.ts';
import { workspaceTokens } from './workspaceTokens/index.mjs';

const storageKey = `getAppsAndDevices:data`;

const trigger = ref(0);

const data = computed<Awaited<ReturnType<typeof getAppsAndDevices>> | null>({
  get: () => {
    if (!isBrowser || !prefixUserNS('').userNamespace) {
      return null;
    }

    trigger.value;
    const storageValue = localStorage.getItem(prefixUserNS(storageKey));
    if (storageValue) {
      try {
        const deserialized = parse(storageValue, {
          URL: (href: string) => new URL(href),
        });
        return deserialized;
      } catch {}
    }
    return null;
  },
  set: (value) => {
    if (!isBrowser || !prefixUserNS('').userNamespace) {
      return;
    }

    if (value) {
      const serialized = stringify(value, {
        URL: (value: unknown) => value instanceof URL && value.href,
      });
      localStorage.setItem(prefixUserNS(storageKey), serialized);
    } else {
      localStorage.removeItem(prefixUserNS(storageKey));
    }
    trigger.value++;
  },
});

if (isBrowser) {
  window.addEventListener('storage', (event) => {
    if (event.key === prefixUserNS(storageKey)) {
      trigger.value++;
    }
  });
}

const loading = ref(false);
const error = ref<unknown>();

/**
 * Fetches every registered external workspace and returns the ones that loaded successfully.
 *
 * Registered external workspaces (and their auth tokens) are stored encrypted behind a PIN, so
 * this prompts to unlock them if they haven't been unlocked yet this session. If the user
 * cancels (or unlocking otherwise fails), external workspaces are simply skipped rather than
 * failing the whole workspace load, since the local workspace's data is still valid on its own.
 */
async function getExternalWorkspacesData(hidePortsWhenPossible: boolean) {
  if (!workspaceTokens.isInitialized) {
    try {
      await workspaceTokens.decryptPersistedTokens();
    } catch (err) {
      console.debug('Skipping external workspaces; not unlocked:', err);
      return [];
    }
  }

  const { iisBase } = useCoreDataStore();
  const results = await Promise.allSettled(
    workspaceTokens.list().map(({ endpoint }) =>
      getExternalWorkspaceAppsAndDevices(iisBase, endpoint, workspaceTokens.getCredentials(endpoint), {
        hidePortsWhenPossible,
      })
    )
  );

  return results.flatMap((result) => {
    if (result.status === 'rejected') {
      console.error('Failed to fetch an external workspace:', result.reason);
      return [];
    }
    console.log('Fetched external workspace successfully:', result.value);
    return [result.value];
  });
}

/**
 * Merges the resources, terminal servers, and folders from registered external workspaces into
 * the local workspace's data. IDs from external workspaces are already namespaced (see
 * `getExternalWorkspaceAppsAndDevices`) so they cannot collide with the local workspace's IDs.
 */
function mergeWorkspaceData(
  local: Awaited<ReturnType<typeof getAppsAndDevices>>,
  externals: Awaited<ReturnType<typeof getExternalWorkspaceAppsAndDevices>>[]
) {
  if (externals.length === 0) {
    return local;
  }

  const terminalServers = new Map(local.terminalServers);
  const resources = [...local.resources];
  const folders: typeof local.folders = { ...local.folders };

  for (const external of externals) {
    for (const [id, name] of external.terminalServers) {
      terminalServers.set(id, name);
    }
    resources.push(...external.resources);
    for (const [folder, folderResources] of Object.entries(external.folders)) {
      folders[folder] = [...(folders[folder] ?? []), ...folderResources].sort((a, b) =>
        a.title.localeCompare(b.title)
      );
    }
  }

  return { ...local, terminalServers, resources, folders };
}

async function getData(
  base?: string,
  { mergeTerminalServers = true, hidePortsWhenPossible = false, supportsCentralizedPublishing = false } = {}
) {
  loading.value = true;

  return getAppsAndDevices(base, {
    mergeTerminalServers,
    redirect: !data.value,
    hidePortsWhenPossible,
    supportsCentralizedPublishing,
  })
    .then(async (result) => {
      const externals = await getExternalWorkspacesData(hidePortsWhenPossible);
      data.value = mergeWorkspaceData(result, externals);
      error.value = null;
    })
    .catch((err) => {
      console.error('Error fetching apps and devices:', err);
      error.value = err;
    })
    .finally(() => {
      loading.value = false;
    });
}

const hasRunAtLeastOnce = ref(false);

interface UseWebfeedDataOptions {
  mergeTerminalServers?: WritableComputedRef<boolean>;
  hidePortsWhenPossible?: WritableComputedRef<boolean>;
  supportsCentralizedPublishing?: ComputedRef<boolean>;
}

export function useWebfeedData(
  base?: string,
  { mergeTerminalServers, hidePortsWhenPossible, supportsCentralizedPublishing }: UseWebfeedDataOptions = {}
) {
  // whenever this function is first called,
  // update the data, even if it is cached
  // in localStorage
  if (!hasRunAtLeastOnce.value) {
    getData(base, {
      mergeTerminalServers: mergeTerminalServers?.value,
      hidePortsWhenPossible: hidePortsWhenPossible?.value,
      supportsCentralizedPublishing: supportsCentralizedPublishing?.value,
    });
    hasRunAtLeastOnce.value = true;
  }

  return {
    data,
    loading,
    error,
    refresh: async ({ mergeTerminalServers, hidePortsWhenPossible }: UseWebfeedDataOptions = {}) => {
      await getData(base, {
        mergeTerminalServers: mergeTerminalServers?.value,
        hidePortsWhenPossible: hidePortsWhenPossible?.value,
        supportsCentralizedPublishing: supportsCentralizedPublishing?.value,
      });
      return { data, loading, error };
    },
  };
}
