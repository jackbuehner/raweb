import { promptForPin } from '$dialogs';
import { getExternalWorkspaceTokens } from './getExternalWorkspaceTokens';
import { setExternalWorkspaceTokens } from './setExternalWorkspaceTokens';

export class ExternalWorkspaceTokens {
  #workspaces = new Map<string, { token: string; name: string }>();
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

    decryptedTokens.forEach(({ endpoint, token, name }) => {
      this.#workspaces.set(endpoint, { token, name });
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

    const tokensArray = Array.from(this.#workspaces.entries()).map(([endpoint, { token, name }]) => ({
      endpoint,
      token,
      name,
    }));
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

  getToken(endpoint: string): string | undefined {
    this.#assertInitialized();

    return this.#workspaces.get(endpoint)?.token;
  }

  setToken(endpoint: string, token: string): void {
    this.#assertInitialized();

    const existing = this.#workspaces.get(endpoint);
    this.#workspaces.set(endpoint, { token, name: existing?.name ?? '' });
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
   * Registers a new external workspace, or renames an existing one if the endpoint
   * is already registered. Does not affect the stored auth token for the endpoint.
   */
  registerWorkspace(endpoint: string, name: string): void {
    this.#assertInitialized();

    const existing = this.#workspaces.get(endpoint);
    this.#workspaces.set(endpoint, { token: existing?.token ?? '', name });
    this.#persistTokens();
  }

  /**
   * Removes an external workspace registration, along with any stored auth token for it.
   */
  unregisterWorkspace(endpoint: string): void {
    this.#assertInitialized();

    this.#workspaces.delete(endpoint);
    this.#persistTokens();
  }
}
