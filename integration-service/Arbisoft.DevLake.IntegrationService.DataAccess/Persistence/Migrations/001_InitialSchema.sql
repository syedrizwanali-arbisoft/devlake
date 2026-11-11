-- Initial schema for the Integration Service database.
-- Times are UTC. UUID strings use VARCHAR(36): MySqlConnector maps CHAR(36) to Guid.
-- This script is purely additive. To roll it back, drop the tables in reverse order of creation.

CREATE TABLE EmailMappings (
  id BIGINT NOT NULL AUTO_INCREMENT,
  arbisoftEmail VARCHAR(255) NOT NULL,
  email VARCHAR(255) NOT NULL,
  createdOn DATETIME(6) NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY UX_EmailMappings_arbisoftEmail_email (arbisoftEmail, email)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE Clients (
  id BIGINT NOT NULL AUTO_INCREMENT,
  clientId VARCHAR(100) NOT NULL,
  name VARCHAR(200) NOT NULL,
  isActive TINYINT(1) NOT NULL DEFAULT 1,
  accessTokenLifetimeSeconds INT NOT NULL DEFAULT 3600,
  createdOn DATETIME(6) NOT NULL,
  createdBy VARCHAR(255) NOT NULL,
  modifiedOn DATETIME(6) NULL,
  modifiedBy VARCHAR(255) NULL,
  PRIMARY KEY (id),
  UNIQUE KEY UX_Clients_clientId (clientId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE Scopes (
  id BIGINT NOT NULL AUTO_INCREMENT,
  name VARCHAR(100) NOT NULL,
  description VARCHAR(500) NOT NULL DEFAULT '',
  createdOn DATETIME(6) NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY UX_Scopes_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE SigningKeys (
  id BIGINT NOT NULL AUTO_INCREMENT,
  kid VARCHAR(100) NOT NULL,
  algorithm VARCHAR(20) NOT NULL,
  publicKey TEXT NOT NULL,
  privateKeyRef VARCHAR(255) NOT NULL,
  activatedOn DATETIME(6) NOT NULL,
  retiredOn DATETIME(6) NULL,
  createdOn DATETIME(6) NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY UX_SigningKeys_kid (kid)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- secret holds a hash of the client secret, never the plain value.
CREATE TABLE ClientSecrets (
  id BIGINT NOT NULL AUTO_INCREMENT,
  clientRefId BIGINT NOT NULL,
  secret VARCHAR(255) NOT NULL,
  expiresOn DATETIME(6) NULL,
  revokedOn DATETIME(6) NULL,
  lastUsedOn DATETIME(6) NULL,
  createdOn DATETIME(6) NOT NULL,
  createdBy VARCHAR(255) NOT NULL,
  PRIMARY KEY (id),
  KEY IX_ClientSecrets_clientRefId (clientRefId),
  CONSTRAINT FK_ClientSecrets_Clients FOREIGN KEY (clientRefId) REFERENCES Clients (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE ClientScopes (
  id BIGINT NOT NULL AUTO_INCREMENT,
  clientRefId BIGINT NOT NULL,
  scopeId BIGINT NOT NULL,
  createdOn DATETIME(6) NOT NULL,
  createdBy VARCHAR(255) NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY UX_ClientScopes_clientRefId_scopeId (clientRefId, scopeId),
  KEY IX_ClientScopes_scopeId (scopeId),
  CONSTRAINT FK_ClientScopes_Clients FOREIGN KEY (clientRefId) REFERENCES Clients (id),
  CONSTRAINT FK_ClientScopes_Scopes FOREIGN KEY (scopeId) REFERENCES Scopes (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE AccessTokens (
  id BIGINT NOT NULL AUTO_INCREMENT,
  jti VARCHAR(36) NOT NULL,
  clientRefId BIGINT NOT NULL,
  clientSecretId BIGINT NOT NULL,
  signingKeyId BIGINT NOT NULL,
  grantedScopes VARCHAR(1000) NOT NULL,
  issuedOn DATETIME(6) NOT NULL,
  expiresOn DATETIME(6) NOT NULL,
  revokedOn DATETIME(6) NULL,
  revokedReason VARCHAR(500) NULL,
  issuedToIp VARCHAR(45) NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY UX_AccessTokens_jti (jti),
  KEY IX_AccessTokens_clientRefId_issuedOn (clientRefId, issuedOn),
  KEY IX_AccessTokens_clientSecretId (clientSecretId),
  KEY IX_AccessTokens_signingKeyId (signingKeyId),
  CONSTRAINT FK_AccessTokens_Clients FOREIGN KEY (clientRefId) REFERENCES Clients (id),
  CONSTRAINT FK_AccessTokens_ClientSecrets FOREIGN KEY (clientSecretId) REFERENCES ClientSecrets (id),
  CONSTRAINT FK_AccessTokens_SigningKeys FOREIGN KEY (signingKeyId) REFERENCES SigningKeys (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE RequestLogs (
  id BIGINT NOT NULL AUTO_INCREMENT,
  clientRefId BIGINT NOT NULL,
  correlationId VARCHAR(36) NOT NULL,
  url VARCHAR(1000) NOT NULL,
  status VARCHAR(50) NOT NULL,
  eventType VARCHAR(100) NOT NULL,
  createdOn DATETIME(6) NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY UX_RequestLogs_correlationId (correlationId),
  KEY IX_RequestLogs_status_createdOn (status, createdOn),
  KEY IX_RequestLogs_clientRefId_createdOn (clientRefId, createdOn),
  CONSTRAINT FK_RequestLogs_Clients FOREIGN KEY (clientRefId) REFERENCES Clients (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE RequestLogJsons (
  id BIGINT NOT NULL AUTO_INCREMENT,
  requestLogId BIGINT NOT NULL,
  json JSON NOT NULL,
  PRIMARY KEY (id),
  KEY IX_RequestLogJsons_requestLogId (requestLogId),
  CONSTRAINT FK_RequestLogJsons_RequestLogs FOREIGN KEY (requestLogId) REFERENCES RequestLogs (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE RequestResponseAttempts (
  id BIGINT NOT NULL AUTO_INCREMENT,
  requestLogId BIGINT NOT NULL,
  responseJson LONGTEXT NOT NULL,
  attemptStatus VARCHAR(50) NOT NULL,
  failureError TEXT NULL,
  attemptBy VARCHAR(255) NOT NULL,
  attemptOn DATETIME(6) NOT NULL,
  PRIMARY KEY (id),
  KEY IX_RequestResponseAttempts_requestLogId (requestLogId),
  CONSTRAINT FK_RequestResponseAttempts_RequestLogs FOREIGN KEY (requestLogId) REFERENCES RequestLogs (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Outbound secrets and API tokens are stored encrypted. One config per client.
CREATE TABLE ClientConfigs (
  id BIGINT NOT NULL AUTO_INCREMENT,
  clientRefId BIGINT NOT NULL,
  authType VARCHAR(50) NOT NULL,
  baseUrl VARCHAR(500) NOT NULL,
  tokenUrl VARCHAR(500) NULL,
  outboundClientId VARCHAR(255) NULL,
  outboundClientSecretEncrypted TEXT NULL,
  outboundScope VARCHAR(255) NULL,
  apiTokenEncrypted TEXT NULL,
  tokenExpiresOn DATETIME(6) NULL,
  tokenRotatedOn DATETIME(6) NULL,
  maxAttempts INT NOT NULL DEFAULT 3,
  isActive TINYINT(1) NOT NULL DEFAULT 1,
  createdOn DATETIME(6) NOT NULL,
  createdBy VARCHAR(255) NOT NULL,
  modifiedOn DATETIME(6) NULL,
  modifiedBy VARCHAR(255) NULL,
  PRIMARY KEY (id),
  UNIQUE KEY UX_ClientConfigs_clientRefId (clientRefId),
  CONSTRAINT FK_ClientConfigs_Clients FOREIGN KEY (clientRefId) REFERENCES Clients (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE ClientConfigOutboundUrls (
  id BIGINT NOT NULL AUTO_INCREMENT,
  clientConfigId BIGINT NOT NULL,
  eventType VARCHAR(100) NOT NULL,
  path VARCHAR(500) NOT NULL,
  method VARCHAR(10) NOT NULL,
  isActive TINYINT(1) NOT NULL DEFAULT 1,
  createdOn DATETIME(6) NOT NULL,
  createdBy VARCHAR(255) NOT NULL,
  modifiedOn DATETIME(6) NULL,
  modifiedBy VARCHAR(255) NULL,
  PRIMARY KEY (id),
  KEY IX_ClientConfigOutboundUrls_clientConfigId_eventType (clientConfigId, eventType),
  CONSTRAINT FK_ClientConfigOutboundUrls_ClientConfigs FOREIGN KEY (clientConfigId) REFERENCES ClientConfigs (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
