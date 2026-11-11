-- Seeds the two scopes the APIs require so the admin portal can assign them without typing the names.
-- INSERT IGNORE keeps the script safe if an admin already created one. To roll back, delete the two rows.

INSERT IGNORE INTO Scopes (name, description, createdOn) VALUES
  ('stats:read', 'Request engineer stats and check the status of a request.', UTC_TIMESTAMP(6)),
  ('projects:fetch', 'Fetch projects and team lead data from Workstream.', UTC_TIMESTAMP(6));
