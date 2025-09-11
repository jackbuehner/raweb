declare global {
  interface Window {
    __namespace: string;

    /** The base path of the IIS application. It will always start and end with a forward slash. @example `/RAWeb/` */
    __iisBase: string;

    /** The base path of the vue application. It will always start and end with a forward slash. @example `/RAWeb/app/` */
    __base: string;

    /** The doman and username of the current authenticated user, from the .ASPXAUTH cookie */
    __authUser: {
      username: string;
      domain: string;
      fullName: string;
      isLocalAdministrator: boolean;
    };

    /** An object that can be used to convert terminal server names to their aliases */
    __terminalServerAliases: Record<string, string>;

    /** Policies that affect app settings for all users. They can be configured in Web.config. */
    __policies: {
      combineTerminalServersModeEnabled: string;
      favoritesEnabled: string;
      flatModeEnabled: string;
      hidePortsEnabled: string;
      iconBackgroundsEnabled: string;
      simpleModeEnabled: string;
      passwordChangeEnabled: string;
      signedInUserGlobalAlerts: string;
    };
    /** The machine name (`Environment.MachineName`). If it has an alias, it is used instead. */
    __machineName: string;
    /** The machine name (`Environment.MachineName`) */
    __envMachineName: string;
    /** The current version of RAWeb */
    __coreVersion: string;
    /** The build timestamp for the frontend */
    __webVersion: string;
  }
}

export {};
