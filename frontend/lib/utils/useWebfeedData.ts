import { parse, stringify } from 'devalue';
import { computed, ref, WritableComputedRef } from 'vue';
import { _getFolders, getAppsAndDevices } from './getAppsAndDevices.ts';

const storageKey = `${window.__namespace}::getAppsAndDevices:data`;

const trigger = ref(0);

const customSerializers = {
  URL: (value: unknown) => value instanceof URL && value.href,
};
const customDeserializers = {
  URL: (href: string) => new URL(href),
};

const data = computed<Awaited<ReturnType<typeof getAppsAndDevices>> | null>({
  get: () => {
    trigger.value;
    const storageValue = localStorage.getItem(storageKey);
    if (storageValue) {
      try {
        const deserialized = parse(storageValue, customDeserializers);
        return deserialized;
      } catch {}
    }
    return null;
  },
  set: (value) => {
    if (value) {
      const serialized = stringify(value, customSerializers);
      localStorage.setItem(storageKey, serialized);
    } else {
      localStorage.removeItem(storageKey);
    }
    trigger.value++;
  },
});

window.addEventListener('storage', (event) => {
  if (event.key === storageKey) {
    trigger.value++;
  }
});

const loading = ref(false);
const error = ref<unknown>();

async function getData(base?: (string | URL)[] | undefined, { mergeTerminalServers = true } = {}) {
  loading.value = true;

  const process = async (base: string | URL | undefined, index: number) => {
    return await getAppsAndDevices(base, { mergeTerminalServers, redirect: index === 0 })
      .then((result) => {
        return result;
      })
      .catch((err) => {
        if (err instanceof Error && err.message === 'ResourceCollection not found in the feed.') {
          // this usually means the authentication failed for this workspace
          // TODO: handle this case more gracefully
          console.warn('Skipping this workspace due to authentication errors:', base?.toString());
          return null;
        } else {
          console.error('Error fetching apps and devices:', err);
          error.value = err;
          return null;
        }
      });
  };

  const promises = (base || [undefined]).map(process);
  return Promise.all(promises)
    .then((results) => results.filter((result) => result !== null))
    .then((results) => {
      const mergedResults = results.slice(1).reduce((merged, current) => {
        const copy = parse(stringify(merged, customSerializers), customDeserializers) as typeof merged;

        if (current.schemaVersion !== copy.schemaVersion) {
          console.warn(
            `Schema version mismatch: current (${current.schemaVersion}) does not match copy (${copy.schemaVersion})`
          );
          return merged;
        }

        // preserve the latest published date
        if (current.publishedDate > copy.publishedDate) {
          copy.publishedDate = current.publishedDate;
        }

        // merge the terminal servers id-name map
        current.terminalServers.forEach((id, name) => {
          copy.terminalServers.set(id, name || id);
        });

        // merge the resources
        current.resources.forEach((resource) => {
          const existingMatchingResource = copy.resources.find((r) => r.id === resource.id);
          if (existingMatchingResource) {
            // merge the hosts, exlcuding duplicate terminal server ids
            const hostsMap = new Map(existingMatchingResource.hosts.map((host) => [host.id, host]));
            resource.hosts.forEach((host) => {
              if (!hostsMap.has(host.id)) {
                hostsMap.set(host.id, host);
              }
            });
            existingMatchingResource.hosts = Array.from(hostsMap.values());
          } else {
            // otherwise, add the resource
            copy.resources.push(resource);
          }
        });

        return copy;
      }, results[0]);

      // re-build the folders
      mergedResults.folders = _getFolders(mergedResults.resources);

      data.value = mergedResults;
    })
    .catch((err) => {
      console.error('Error processing apps and devices:', err);
      error.value = err;
    })
    .finally(() => {
      loading.value = false;
    });
}

const hasRunAtLeastOnce = ref(false);

interface UseWebfeedDataOptions {
  mergeTerminalServers?: WritableComputedRef<boolean>;
}

export function useWebfeedData(
  base?: (string | URL)[] | undefined,
  { mergeTerminalServers }: UseWebfeedDataOptions = {}
) {
  // whenever this function is first called,
  // update the data, even if it is cached
  // in localStorage
  if (!hasRunAtLeastOnce.value) {
    getData(base, { mergeTerminalServers: mergeTerminalServers?.value });
    hasRunAtLeastOnce.value = true;
  }

  return {
    data,
    loading,
    error,
    refresh: async ({ mergeTerminalServers }: UseWebfeedDataOptions = {}) => {
      await getData(base, { mergeTerminalServers: mergeTerminalServers?.value });
      return { data, loading, error };
    },
  };
}
