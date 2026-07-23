import { promptForPin } from '$dialogs';
import { getExternalWorkspaceTokens } from './getExternalWorkspaceTokens';
import { setExternalWorkspaceTokens } from './setExternalWorkspaceTokens';

export class ExternalWorkspaceTokens {
  #workspaces = new Map<string, { username: string; password: string; name: string }>();
  #hasBeenInitialized = false;
  #_decryptionKey: string | undefined = undefined;

  constructor(init = false) {
    if (init !== false) {
      this.decryptPersistedTokens();
    }
  }

  get isInitialized(): boolean {
    return this.#hasBeenInitialized;
  }

  async decryptPersistedTokens(): Promise<void> {
    const decryptionKey = await this.#decryptionKey;

    if (!decryptionKey) {
      throw new Error('Decryption key is missing');
    }

    const decryptedTokens = await getExternalWorkspaceTokens(decryptionKey).catch((err) => {
      console.error('Error decrypting external workspace tokens:', err);
      return null;
    });
    if (!decryptedTokens) {
      this.#_decryptionKey = undefined;
      throw new Error('Failed to decrypt external workspace tokens');
    }

    decryptedTokens.forEach(({ endpoint, username, password, name }) => {
      this.#workspaces.set(endpoint, { username, password, name });
    });

    this.#hasBeenInitialized = true;
  }

  get #decryptionKey() {
    return (async () => {
      async function canDecryptTokens(decryptionKey: string) {
        const encryptedTokens = localStorage.getItem('externalWorkspaceTokens');
        if (!encryptedTokens) {
          return true; // No tokens to decrypt, so any key is valid
        }

        const decryptedTokens = await getExternalWorkspaceTokens(decryptionKey);
        return !!decryptedTokens;
      }

      if (!this.#_decryptionKey) {
        this.#_decryptionKey = await promptForPin(
          'RemoteApps and Devices needs your permission to load credentials for your external workspaces.',
          (pin) => {
            try {
              return canDecryptTokens(pin);
            } catch (err) {
              console.error('Error checking decryption key:', err);
              throw new Error('Error checking decryption key');
            }
          }
        );
      }

      if (!this.#_decryptionKey) {
        throw new Error('Decryption key is missing');
      }

      return this.#_decryptionKey;
    })();
  }

  #persistTokens() {
    if (!this.#hasBeenInitialized) {
      throw new Error('ExternalWorkspaceTokens has not been initialized. Call decryptPersistedTokens() first.');
    }

    const tokensArray = Array.from(this.#workspaces.entries()).map(
      ([endpoint, { username, password, name }]) => ({
        endpoint,
        username,
        password,
        name,
      })
    );
    const encryptionKey = this.#_decryptionKey;

    if (!encryptionKey) {
      throw new Error('Encryption key is missing');
    }

    return setExternalWorkspaceTokens(tokensArray, encryptionKey);
  }

  #assertInitialized() {
    if (!this.#hasBeenInitialized) {
      throw new Error('ExternalWorkspaceTokens has not been initialized. Call decryptPersistedTokens() first.');
    }
  }

  /**
   * Returns the stored Windows/NTLM credentials for an external workspace, if any.
   */
  getCredentials(endpoint: string): { username: string; password: string } | undefined {
    this.#assertInitialized();

    const workspace = this.#workspaces.get(endpoint);
    if (!workspace) {
      return undefined;
    }

    return { username: workspace.username, password: workspace.password };
  }

  /**
   * Sets the Windows/NTLM credentials to use for an external workspace.
   */
  setCredentials(endpoint: string, username: string, password: string): void {
    this.#assertInitialized();

    const existing = this.#workspaces.get(endpoint);
    this.#workspaces.set(endpoint, { username, password, name: existing?.name ?? '' });
    this.#persistTokens();
  }

  /**
   * Lists all registered external workspaces (endpoint + display name).
   */
  list(): { endpoint: string; name: string }[] {
    this.#assertInitialized();

    return Array.from(this.#workspaces.entries()).map(([endpoint, { name }]) => ({ endpoint, name }));
  }

  /**
   * Registers a new external workspace (with its Windows/NTLM credentials), or updates an
   * existing one if the endpoint is already registered.
   */
  registerWorkspace(endpoint: string, name: string, username: string, password: string): void {
    this.#assertInitialized();

    this.#workspaces.set(endpoint, { username, password, name });
    this.#persistTokens();
  }

  /**
   * Removes an external workspace registration, along with its stored credentials.
   */
  unregisterWorkspace(endpoint: string): void {
    this.#assertInitialized();

    this.#workspaces.delete(endpoint);
    this.#persistTokens();
  }
}
