import { defineStore } from 'pinia';

interface State extends EmptyState {
  userNamespace: string;

  /** The base path of the IIS application. It will always start and end with a forward slash. @example `/RAWeb/` */
  iisBase: string;

  /** The base path of the vue application. It will always start and end with a forward slash. @example `/RAWeb/app/` */
  appBase: string;

  /** The doman and username of the current authenticated user, from the .ASPXAUTH cookie */
  authUser: {
    username: string;
    domain: string;
    fullName: string;
    isLocalAdministrator: boolean;
  };

  /** An object that can be used to convert terminal server names to their aliases */
  terminalServerAliases: Record<string, string>;

  /** Policies that affect app settings for all users. They can be configured in Web.config. */
  policies: {
    combineTerminalServersModeEnabled: boolean | null;
    favoritesEnabled: boolean | null;
    flatModeEnabled: boolean | null;
    hidePortsEnabled: boolean | null;
    iconBackgroundsEnabled: boolean | null;
    simpleModeEnabled: boolean | null;
    passwordChangeEnabled: boolean | null;
    signedInUserGlobalAlerts: string | null;
  };

  /** The machine name (`Environment.MachineName`). If it has an alias, it is used instead. */
  machineName: string;

  /** The machine name (`Environment.MachineName`) */
  envMachineName: string;

  /** The current version of RAWeb Server */
  coreVersion: string;

  /** The build timestamp for the web client */
  webVersion: string;
}

interface EmptyState {
  initialized: boolean;
  initializing?: boolean;
}

export const useCoreDataStore = defineStore('coreData', {
  state: (): State => ({ initialized: false } as State), // cast because we will pre-fetch the data before the app is mounted
  actions: {
    async fetchData() {
      // only fetch once
      if (this.initialized || this.initializing) {
        return;
      }

      this.initializing = true;
      await fetch(`./api/app-init-details`)
        .then((res) => res.json())
        .then((data) => {
          if (typeof data !== 'object' || data === null) {
            throw new Error('Invalid data');
          }
          Object.assign(this, data);
          this.initialized = true;
        })
        .finally(() => {
          this.initializing = false;
        });
    },
  },
});
