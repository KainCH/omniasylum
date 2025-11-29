const { SecretClient } = require('@azure/keyvault-secrets');
const { DefaultAzureCredential } = require('@azure/identity');

/**
 * Azure Key Vault integration
 * Falls back to environment variables for local development
 */
class KeyVault {
  constructor() {
    this.client = null;
    this.useKeyVault = false;
    this.initialize();
  }

  /**
   * Initialize Key Vault client
   */
  async initialize() {
    const keyVaultName = process.env.AZURE_KEYVAULT_NAME;

    if (keyVaultName) {
      try {
        const vaultUrl = `https://${keyVaultName}.vault.azure.net`;

        // Use User Assigned Managed Identity with specific client ID
        const credential = new DefaultAzureCredential({
          managedIdentityClientId: 'b72c3d28-61d4-4c35-bac8-5e4928de2c7e'
        });

        this.client = new SecretClient(vaultUrl, credential);
        this.useKeyVault = true;
        console.log('✅ Connected to Azure Key Vault with User Assigned Managed Identity');
      } catch (error) {
        console.warn('⚠️  Failed to connect to Key Vault, using environment variables:', error?.message);
        this.useKeyVault = false;
      }
    } else {
      console.log('ℹ️  Azure Key Vault not configured, using environment variables');
      this.useKeyVault = false;
    }
  }

  /**
   * Get a secret from Key Vault or environment variable
   * @param {string} secretName - Name of secret (e.g., 'TWITCH-CLIENT-ID')
   * @returns {Promise<string>} Secret value
   */
  async getSecret(secretName) {
    if (this.useKeyVault && this.client) {
      try {
        const secret = await this.client.getSecret(secretName);
        return secret.value;
      } catch (error) {
        console.warn(`⚠️  Failed to get ${secretName} from Key Vault:`, error?.message);
        // Fall back to environment variable
        return this.getFromEnv(secretName);
      }
    } else {
      return this.getFromEnv(secretName);
    }
  }

  /**
   * Get value from environment variable
   * Converts Key Vault naming (TWITCH-CLIENT-ID) to env naming (TWITCH_CLIENT_ID)
   */
  getFromEnv(secretName) {
    const envName = secretName.replace(/-/g, '_');
    return process.env[envName];
  }

  /**
   * Set a secret in Key Vault (for administrative use)
   * @param {string} secretName - Name of secret
   * @param {string} secretValue - Value to store
   */
  async setSecret(secretName, secretValue) {
    if (this.useKeyVault && this.client) {
      try {
        await this.client.setSecret(secretName, secretValue);
        console.log(`✅ Secret ${secretName} stored in Key Vault`);
        return true;
      } catch (error) {
        console.error(`❌ Failed to set ${secretName} in Key Vault:`, error);
        return false;
      }
    } else {
      console.warn('⚠️  Key Vault not available, cannot store secret');
      return false;
    }
  }

  /**
   * Check if Key Vault is being used
   */
  isUsingKeyVault() {
    return this.useKeyVault;
  }
}

// Export singleton instance
module.exports = new KeyVault();
