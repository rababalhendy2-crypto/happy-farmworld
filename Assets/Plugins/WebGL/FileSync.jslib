// Writes the in-memory browser file system to IndexedDB, so the save file survives a reload or a closed tab.
mergeInto(LibraryManager.library, {
  FW_SyncFiles: function () {
    FS.syncfs(false, function (err) { if (err) console.log('[FarmWorld] file sync failed: ' + err); });
  }
});
