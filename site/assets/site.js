/*
  Two small jobs, and the page works with neither of them done.

  Everything here is an improvement on a page that is already complete: the reference lists every
  command with no script running, and the download page already links to the releases page, which
  always shows the newest release whether or not this file loads.
*/

(function () {
  "use strict";

  // ---- 1. Filtering the command reference ---------------------------------------------------
  //
  // All of the commands are in the page. This hides the ones that do not match, which is why it
  // can be instant and why it still works with the network gone.
  var search = document.getElementById("command-search");
  if (search) {
    var commands = Array.prototype.slice.call(document.querySelectorAll(".command"));
    var groups = Array.prototype.slice.call(document.querySelectorAll("[data-group]"));
    var count = document.getElementById("command-count");
    var empty = document.getElementById("command-empty");

    var apply = function () {
      var words = search.value.toLowerCase().split(/\s+/).filter(Boolean);
      var shown = 0;

      commands.forEach(function (item) {
        var haystack = item.getAttribute("data-search") || "";
        // Every word must match, not any of them: somebody typing "photo caption" means both, and
        // answering with every photo command would bury the one thing they asked for. This is the
        // rule the app's own search uses (HelpIndex.Search).
        var hit = words.every(function (word) { return haystack.indexOf(word) !== -1; });
        item.hidden = !hit;
        if (hit) { shown++; }
      });

      // A heading with nothing under it reads as a section that has been emptied rather than one
      // that does not match.
      groups.forEach(function (group) {
        var any = group.querySelector(".command:not([hidden])");
        group.hidden = !any;
      });

      if (empty) { empty.hidden = shown !== 0; }

      if (count) {
        count.textContent = words.length === 0
          ? ""
          : shown === 1 ? "1 command matches." : shown + " commands match.";
      }
    };

    search.addEventListener("input", apply);
    apply();
  }

  // ---- 2. Naming the release the download buttons lead to -----------------------------------
  //
  // The page ships with the last version known when it was built. If GitHub answers, the real
  // newest version replaces it. If it does not answer — offline, blocked, rate-limited — the page
  // keeps what it had, which is a true statement about a release that exists rather than an error
  // message about one that might.
  var slots = document.querySelectorAll("[data-latest-version]");
  if (slots.length && window.fetch) {
    fetch("https://api.github.com/repos/donaldsteele/TrestleBoard/releases/latest", {
      headers: { Accept: "application/vnd.github+json" }
    })
      .then(function (response) { return response.ok ? response.json() : null; })
      .then(function (release) {
        if (!release || typeof release.tag_name !== "string") { return; }
        var version = release.tag_name.replace(/^v/, "");
        Array.prototype.forEach.call(slots, function (slot) { slot.textContent = version; });
      })
      .catch(function () { /* Keep the built-in version. */ });
  }
})();
