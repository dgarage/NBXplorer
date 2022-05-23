CREATE OR REPLACE VIEW descriptors_scripts_unused AS
  SELECT ds.code, ds.descriptor, ds.script, ds.idx, s.addr, d.metadata d_metadata, ds.metadata ds_metadata FROM descriptors_scripts ds
  JOIN scripts s USING (code, script)
  JOIN descriptors d
  USING (code, descriptor)
  WHERE ds.used IS FALSE;
