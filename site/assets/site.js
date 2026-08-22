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

  // ---- 2. Guessing which download this computer needs ---------------------------------------
  //
  // An improvement on a page that is already right. All four choices are in the markup, each saying
  // who it is for, so with this script off — or when the guess cannot be made — the reader still has
  // everything they need. What this adds is a badge on the likely one and a sentence at the top
  // saying what we think they are on and how to disagree with it.
  //
  // IT NEVER HIDES A CHOICE, AND IT NEVER FOLLOWS A LINK FOR ANYBODY. Guessing wrong is normal;
  // guessing wrong and having downloaded 61MB of the wrong thing is not.
  var list = document.querySelector(".downloads");
  var verdict = document.getElementById("os-verdict");

  if (list && verdict) {
    var say = function (which, sentence) {
      verdict.textContent = sentence;
      if (!which) { return; }

      var card = list.querySelector('[data-os="' + which + '"]');
      if (!card) { return; }

      card.classList.add("is-recommended");
      list.classList.add("has-recommendation");

      // The word, not only the bar and the border — colour is never the only signal.
      var flag = document.createElement("p");
      flag.className = "recommended-flag";
      flag.textContent = "Recommended for you";
      card.querySelector(".download-body").insertBefore(
        flag, card.querySelector(".download-name"));

      // Move it to the top, so the one they most likely want is the one they read first.
      list.insertBefore(card, list.firstElementChild);
    };

    var ua = navigator.userAgent || "";
    var data = navigator.userAgentData;
    var platform = (data && data.platform) || "";
    var isMac = /Mac/i.test(platform) || (/Mac OS X/i.test(ua) && !/iPhone|iPad|iPod/i.test(ua));
    var isWindows = /Win/i.test(platform) || /Windows/i.test(ua);
    // Android reports Linux in its user agent, and there is no Android build, so it must not match.
    var isLinux = (/Linux/i.test(platform) || /Linux|X11/i.test(ua)) && !/Android/i.test(ua);
    var isPhone = /Android|iPhone|iPad|iPod/i.test(ua);

    if (isPhone) {
      // Saying "we cannot tell" would be a lie, and recommending a desktop build to a telephone
      // would be worse. TrestleBoard lays out a printed page; it does not run here.
      verdict.textContent =
        "This looks like a phone or tablet. TrestleBoard is a program for a Windows PC, a Mac or a "
        + "Linux computer — open this page on the computer you make the newsletter on.";
    } else if (isWindows) {
      say("windows", "This looks like a Windows PC, so the first one is almost certainly yours.");
    } else if (isLinux) {
      say("linux", "This looks like a Linux computer, so the first one is almost certainly yours.");
    } else if (isMac) {
      // Which Mac is the one question a browser mostly will not answer. Chromium-based browsers
      // will, given a moment; Safari will not, and inventing an answer there would send somebody to
      // a build that does not run. So: narrow it when we can, and otherwise say honestly that there
      // are two and how to tell them apart.
      var both = function () {
        verdict.textContent =
          "This looks like a Mac. There are two Mac downloads and this browser will not say which "
          + "chip you have — the Apple menu, About This Mac, tells you in one line.";
      };

      if (data && data.getHighEntropyValues) {
        data.getHighEntropyValues(["architecture"]).then(function (hints) {
          if (hints && hints.architecture === "arm") {
            say("mac-arm", "This looks like a Mac with Apple silicon, so the first one is yours.");
          } else if (hints && hints.architecture === "x86") {
            say("mac-intel", "This looks like an Intel Mac, so the first one is yours.");
          } else {
            both();
          }
        }).catch(both);
      } else {
        both();
      }
    }
  }

  // ---- 3. Naming the release the download buttons lead to -----------------------------------
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

        // And the real size of each installer, from the release itself. The page ships with an
        // approximate one so it is never blank or wrong-looking offline; this replaces it with the
        // figure for the build the button will actually fetch. Only installers are matched — the
        // portable zips and the delta packages carry the same platform words in their names.
        if (!Array.isArray(release.assets)) { return; }
        Array.prototype.forEach.call(document.querySelectorAll("[data-size]"), function (slot) {
          var key = slot.getAttribute("data-size");
          for (var i = 0; i < release.assets.length; i++) {
            var asset = release.assets[i];
            var name = asset.name || "";
            var installer = /Setup\.(exe|pkg)$|\.AppImage$/.test(name);
            if (installer && name.indexOf(key) !== -1 && typeof asset.size === "number") {
              slot.textContent = Math.round(asset.size / 1048576) + " MB";
              return;
            }
          }
        });
      })
      .catch(function () { /* Keep the built-in version. */ });
  }
})();
