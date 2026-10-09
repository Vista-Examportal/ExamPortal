/*
 * VISTAWAYS TECH shared frontend foundation
 * -----------------------------------------------------------------------
 * Vanilla JS only (no jQuery, no Bootstrap JS) for everything added by the
 * new shared layout: sidebar collapse/mobile drawer, the profile &
 * notification menus, dark mode, toasts, a tiny generic modal helper, and
 * a tiny generic accordion helper (reused by the education fieldset).
 *
 * jQuery / jQuery Validate / jQuery Unobtrusive Validation are untouched
 * and keep running side by side — this file never touches form
 * validation behavior.
 */
(function () {
    "use strict";

    var STORAGE_THEME = "vw-theme";
    var STORAGE_SIDEBAR = "vw-sidebar-collapsed";

    /* ---------------------------------------------------------------- */
    /* Small shared utilities                                            */
    /* ---------------------------------------------------------------- */
    // Escapes text for safe insertion into HTML built client-side (e.g. the
    // skill-tag editor and the education/language rows). Previously this
    // exact helper was copy-pasted into both CompleteProfile.cshtml and
    // UploadResume.cshtml; now there's one definition, exposed globally
    // so per-page <script> blocks can call window.vwEscHtml(...).
    function escHtml(s) {
        return (s || "").replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
    }
    window.vwEscHtml = escHtml;

    /* ---------------------------------------------------------------- */
    /* Icons                                                             */
    /* ---------------------------------------------------------------- */
    function renderIcons() {
        if (window.lucide && typeof window.lucide.createIcons === "function") {
            window.lucide.createIcons();
        }
    }

    /* ---------------------------------------------------------------- */
    /* Dark mode                                                         */
    /* ---------------------------------------------------------------- */
    function getPreferredTheme() {
        var stored = localStorage.getItem(STORAGE_THEME);
        if (stored === "light" || stored === "dark") return stored;
        return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
    }

    function applyTheme(theme) {
        document.documentElement.setAttribute("data-theme", theme);
        document.documentElement.classList.toggle("dark", theme === "dark");
        document.querySelectorAll("[data-theme-icon]").forEach(function (el) {
            el.setAttribute("data-lucide", theme === "dark" ? "sun" : "moon");
        });
        renderIcons();
    }

    function initTheme() {
        applyTheme(getPreferredTheme());
        document.querySelectorAll("[data-theme-toggle]").forEach(function (btn) {
            btn.addEventListener("click", function () {
                var next = document.documentElement.getAttribute("data-theme") === "dark" ? "light" : "dark";
                localStorage.setItem(STORAGE_THEME, next);
                applyTheme(next);
            });
        });
    }

    /* ---------------------------------------------------------------- */
    /* Transparent-over-hero nav (careers landing page only)             */
    /* -----------------------------------------------------------------
       No-op unless the page has a .app-topbar.is-hero-nav (opt-in via
       ViewData["TransparentHero"] in the page, see _Layout.cshtml).
       Toggles .is-scrolled once the page has scrolled roughly past the
       hero section's own height, so the nav crossfades to solid right
       as the hero ends rather than at an arbitrary fixed pixel count. */
    function initHeroNav() {
        var nav = document.querySelector(".app-topbar.is-hero-nav");
        var hero = document.querySelector("[data-hero]");
        if (!nav) return;
        var threshold = hero ? Math.max(hero.offsetHeight - 80, 80) : 400;
        var ticking = false;
        function update() {
            nav.classList.toggle("is-scrolled", window.scrollY > threshold);
            ticking = false;
        }
        update();
        window.addEventListener("scroll", function () {
            if (!ticking) { window.requestAnimationFrame(update); ticking = true; }
        }, { passive: true });
    }

    /* ---------------------------------------------------------------- */
    /* Sidebar (desktop collapse + mobile drawer)                        */
    /* ---------------------------------------------------------------- */
    // Promotes the sidebar link matching the current location.hash to
    // "active", and demotes whichever link the server defaulted to active
    // for that same controller+action (see the isActive() comment in
    // Views/Shared/Components/SidebarNav/Default.cshtml for why this can't
    // be resolved server-side: URL fragments aren't sent in the request).
    // A no-op whenever there's no hash, or no sidebar link declares that
    // fragment via data-nav-fragment.
    function syncSidebarFragmentActive() {
        var hash = window.location.hash ? window.location.hash.slice(1) : "";
        if (!hash) return;
        var match = document.querySelector('.app-sidebar-link[data-nav-fragment="' + hash + '"]');
        if (!match) return;
        document.querySelectorAll(".app-sidebar-link.is-active").forEach(function (el) {
            el.classList.remove("is-active");
            el.removeAttribute("aria-current");
        });
        match.classList.add("is-active");
        match.setAttribute("aria-current", "page");
    }

    function initSidebar() {
        var sidebar = document.getElementById("appSidebar");
        var backdrop = document.getElementById("appSidebarBackdrop");
        if (!sidebar) return;

        var collapsed = localStorage.getItem(STORAGE_SIDEBAR) === "1";
        sidebar.classList.toggle("is-collapsed", collapsed);

        var collapseToggles = document.querySelectorAll("[data-sidebar-collapse-toggle]");
        collapseToggles.forEach(function (btn) {
            btn.setAttribute("aria-pressed", collapsed ? "true" : "false");
            btn.addEventListener("click", function () {
                collapsed = sidebar.classList.toggle("is-collapsed");
                localStorage.setItem(STORAGE_SIDEBAR, collapsed ? "1" : "0");
                collapseToggles.forEach(function (b) { b.setAttribute("aria-pressed", collapsed ? "true" : "false"); });
            });
        });

        var mobileToggles = document.querySelectorAll("[data-sidebar-mobile-toggle]");
        var lastMobileTrigger = null;
        var FOCUSABLE = 'a[href], button:not([disabled]), [tabindex]:not([tabindex="-1"])';

        function trapFocus(e) {
            if (e.key !== "Tab") return;
            var focusable = Array.prototype.slice.call(sidebar.querySelectorAll(FOCUSABLE));
            if (!focusable.length) return;
            var first = focusable[0];
            var last = focusable[focusable.length - 1];
            if (e.shiftKey && document.activeElement === first) {
                e.preventDefault();
                last.focus();
            } else if (!e.shiftKey && document.activeElement === last) {
                e.preventDefault();
                first.focus();
            }
        }

        function openMobile(e) {
            lastMobileTrigger = e ? e.currentTarget : null;
            sidebar.classList.add("is-mobile-open");
            if (backdrop) backdrop.classList.remove("hidden");
            document.body.classList.add("overflow-hidden", "lg:overflow-auto");
            mobileToggles.forEach(function (b) { b.setAttribute("aria-expanded", "true"); });
            sidebar.addEventListener("keydown", trapFocus);
            // Move focus into the drawer so keyboard/screen-reader users
            // land somewhere sensible instead of it silently appearing.
            var closeBtn = sidebar.querySelector("[data-sidebar-mobile-close]");
            (closeBtn || sidebar.querySelector(FOCUSABLE) || sidebar).focus();
        }
        function closeMobile() {
            if (!sidebar.classList.contains("is-mobile-open")) return;
            sidebar.classList.remove("is-mobile-open");
            if (backdrop) backdrop.classList.add("hidden");
            document.body.classList.remove("overflow-hidden");
            mobileToggles.forEach(function (b) { b.setAttribute("aria-expanded", "false"); });
            sidebar.removeEventListener("keydown", trapFocus);
            if (lastMobileTrigger) lastMobileTrigger.focus();
        }
        mobileToggles.forEach(function (btn) {
            btn.setAttribute("aria-expanded", "false");
            btn.addEventListener("click", openMobile);
        });
        document.querySelectorAll("[data-sidebar-mobile-close]").forEach(function (btn) {
            btn.addEventListener("click", closeMobile);
        });
        // Picking a destination from the drawer should close it, same as
        // tapping the backdrop — otherwise it stays open over the page
        // the person just navigated to.
        sidebar.querySelectorAll(".app-sidebar-link").forEach(function (link) {
            link.addEventListener("click", closeMobile);
        });
        if (backdrop) backdrop.addEventListener("click", closeMobile);
        document.addEventListener("keydown", function (e) {
            if (e.key === "Escape") closeMobile();
        });
    }

    /* ---------------------------------------------------------------- */
    /* Generic dropdown menus (profile, notifications)                   */
    /* ---------------------------------------------------------------- */
    function initMenus() {
        var openMenu = null;
        var MENU_ITEM_SELECTOR = 'a, button:not([disabled])';

        function menuItems(panel) {
            return Array.prototype.slice.call(panel.querySelectorAll(MENU_ITEM_SELECTOR));
        }

        function close(opts) {
            if (!openMenu) return;
            var panel = openMenu.panel;
            var trigger = openMenu.trigger;
            var returnFocus = opts && opts.returnFocus;
            trigger.setAttribute("aria-expanded", "false");
            openMenu = null;
            // Play the fade/scale-out before actually hiding, so closing
            // reads as deliberate rather than the panel just vanishing.
            panel.classList.add("is-closing");
            window.setTimeout(function () {
                panel.classList.add("hidden");
                panel.classList.remove("is-closing");
            }, 100);
            if (returnFocus) trigger.focus();
        }

        document.querySelectorAll("[data-menu-trigger]").forEach(function (trigger) {
            var panel = document.getElementById(trigger.getAttribute("data-menu-trigger"));
            if (!panel) return;
            trigger.setAttribute("aria-expanded", "false");
            trigger.setAttribute("aria-haspopup", "true");
            panel.setAttribute("role", "menu");
            menuItems(panel).forEach(function (item) {
                item.classList.add("app-menu-item");
                item.setAttribute("role", "menuitem");
                item.setAttribute("tabindex", "-1");
            });

            trigger.addEventListener("click", function (e) {
                e.stopPropagation();
                var isOpen = openMenu && openMenu.panel === panel;
                close();
                if (!isOpen) {
                    panel.classList.remove("hidden", "is-closing");
                    trigger.setAttribute("aria-expanded", "true");
                    openMenu = { panel: panel, trigger: trigger };
                    var items = menuItems(panel);
                    if (items.length) items[0].focus();
                }
            });

            // Arrow-key roving focus + type-ahead-free Home/End, so the
            // menu is fully operable without a mouse once it's open.
            panel.addEventListener("keydown", function (e) {
                var items = menuItems(panel);
                if (!items.length) return;
                var currentIndex = items.indexOf(document.activeElement);
                if (e.key === "ArrowDown") {
                    e.preventDefault();
                    items[(currentIndex + 1 + items.length) % items.length].focus();
                } else if (e.key === "ArrowUp") {
                    e.preventDefault();
                    items[(currentIndex - 1 + items.length) % items.length].focus();
                } else if (e.key === "Home") {
                    e.preventDefault();
                    items[0].focus();
                } else if (e.key === "End") {
                    e.preventDefault();
                    items[items.length - 1].focus();
                } else if (e.key === "Tab") {
                    // Tabbing out of the panel is a normal way to leave it;
                    // just close without stealing focus back.
                    close();
                }
            });
        });

        document.addEventListener("click", close);
        document.addEventListener("keydown", function (e) {
            if (e.key === "Escape" && openMenu) close({ returnFocus: true });
        });
    }

    /* ---------------------------------------------------------------- */
    /* Toasts — also used to surface TempData Success/Error/Warning/Info  */
    /* messages                                                           */
    /* -----------------------------------------------------------------
       Four variants, each with its own colors, icon, urgency and
       auto-dismiss timing:
         - success : confirms something completed. Polite, brief.
         - error   : something failed. Assertive (announced immediately
                     by screen readers) and stays up longer so it isn't
                     missed.
         - warning : needs attention but isn't a hard failure. Assertive,
                     slightly longer than success/info.
         - info     : neutral, FYI-level detail. Polite, brief.

       A slim progress bar shows time-to-dismiss and pauses on hover or
       keyboard focus so a toast never disappears mid-read. Toasts are
       capped at MAX_TOASTS on screen at once — oldest is retired first —
       so a burst of flash messages can't flood the corner of the screen.
       Signature/behavior for callers is unchanged: window.vwToast(message,
       variant) still works exactly as before; variant just now also
       accepts "warning". */
    var VW_TOAST_VARIANTS = {
        success: {
            icon: "check-circle",
            classes: "border-emerald-200 text-emerald-800 bg-emerald-50 dark:bg-emerald-900/30 dark:text-emerald-200 dark:border-emerald-800",
            duration: 6000,
            assertive: false
        },
        error: {
            icon: "alert-circle",
            classes: "border-red-200 text-red-800 bg-red-50 dark:bg-red-900/30 dark:text-red-200 dark:border-red-800",
            duration: 9000,
            assertive: true
        },
        warning: {
            icon: "alert-triangle",
            classes: "border-amber-200 text-amber-800 bg-amber-50 dark:bg-amber-900/30 dark:text-amber-200 dark:border-amber-800",
            duration: 8000,
            assertive: true
        },
        info: {
            icon: "info",
            classes: "border-slate-200 text-slate-800 bg-white dark:bg-slate-800 dark:text-slate-100 dark:border-slate-700",
            duration: 6000,
            assertive: false
        }
    };
    var VW_MAX_TOASTS = 4;

    function removeToast(toast) {
        if (!toast || toast._vwLeaving) return;
        toast._vwLeaving = true;
        if (toast._vwTimer) clearTimeout(toast._vwTimer);
        toast.classList.add("is-leaving");
        toast.addEventListener("animationend", function () { toast.remove(); }, { once: true });
        // Fallback in case the animationend event doesn't fire (e.g. reduced motion).
        setTimeout(function () { toast.remove(); }, 200);
    }

    function createToast(message, variant) {
        var host = document.getElementById("appToastHost");
        if (!host || !message) return;

        var v = VW_TOAST_VARIANTS[variant] || VW_TOAST_VARIANTS.info;

        // Enforce the on-screen cap before adding the new one.
        var existing = host.querySelectorAll(".app-toast:not(.is-leaving)");
        if (existing.length >= VW_MAX_TOASTS) {
            removeToast(existing[0]);
        }

        var toast = document.createElement("div");
        toast.className = "app-toast pointer-events-auto relative overflow-hidden flex items-start gap-3 rounded-lg border px-4 py-3 pb-[10px] shadow-lg text-sm " + v.classes;
        // Errors and warnings interrupt (assertive); success/info just announce (polite).
        toast.setAttribute("role", v.assertive ? "alert" : "status");
        toast.setAttribute("aria-live", v.assertive ? "assertive" : "polite");
        toast.innerHTML =
            '<i data-lucide="' + v.icon + '" class="h-5 w-5 shrink-0 mt-0.5"></i>' +
            '<span class="flex-1">' + message + "</span>" +
            '<button type="button" class="shrink-0 opacity-60 hover:opacity-100" aria-label="Dismiss notification"><i data-lucide="x" class="h-4 w-4"></i></button>' +
            '<div class="app-toast-progress" style="animation-duration: ' + v.duration + 'ms;"></div>';

        toast.querySelector("button").addEventListener("click", function () { removeToast(toast); });

        // Hovering or focusing (e.g. tabbing to the dismiss button) pauses
        // both the removal timer and the progress-bar animation so a toast
        // never vanishes while someone is actually reading or acting on it.
        var remaining = v.duration;
        var startedAt = Date.now();
        function start() {
            startedAt = Date.now();
            toast._vwTimer = setTimeout(function () { removeToast(toast); }, remaining);
        }
        function pause() {
            if (!toast._vwTimer) return;
            clearTimeout(toast._vwTimer);
            toast._vwTimer = null;
            remaining -= (Date.now() - startedAt);
            toast.classList.add("is-paused");
        }
        function resume() {
            if (toast._vwTimer || remaining <= 0) return;
            toast.classList.remove("is-paused");
            start();
        }
        toast.addEventListener("mouseenter", pause);
        toast.addEventListener("mouseleave", resume);
        toast.addEventListener("focusin", pause);
        toast.addEventListener("focusout", resume);

        host.appendChild(toast);
        renderIcons();
        start();
    }
    window.vwToast = createToast;
    window.vwRenderIcons = renderIcons;

    /* ---------------------------------------------------------------- */
    /* Generic confirmation dialog                                       */
    /* -----------------------------------------------------------------
       Usage: put data-confirm-title="..." data-confirm-message="..."
       (message optional) on any <button type="submit"> inside a <form>.
       Clicking it pops a shared modal; confirming submits the form the
       button belongs to. Cancel / Escape / backdrop click all dismiss it.
       This never touches validation — it only runs after a click, so
       jQuery Unobtrusive Validation still gets first refusal on submit.

       The dialog is now variant-aware — an icon badge + confirm-button
       color communicate what kind of action this is:
         - danger  : destructive / irreversible (delete, remove, revoke…)
         - warning : cautionary but non-destructive (disable, cancel,
                     reject, discard…)
         - default : everything else (retry, mark as read…)
       Variant is picked, in order: an explicit data-confirm-variant,
       then the legacy data-confirm-danger flag (kept for back-compat),
       then a keyword guess from the title, then "default". Existing
       markup that only sets data-confirm-title/-message is untouched
       and keeps working exactly as before — it just now also renders
       with the right icon automatically. */
    var VW_DIALOG_VARIANTS = {
        danger:  { icon: "trash-2",       badgeClass: "ui-modal-icon-danger",  btnClass: "ui-btn-danger" },
        warning: { icon: "alert-triangle", badgeClass: "ui-modal-icon-warning", btnClass: "ui-btn-warning" },
        success: { icon: "check-circle-2", badgeClass: "ui-modal-icon-success", btnClass: "ui-btn-primary" },
        info:    { icon: "info",           badgeClass: "ui-modal-icon-info",    btnClass: "ui-btn-primary" },
        "default": { icon: "help-circle",  badgeClass: "ui-modal-icon-default", btnClass: "ui-btn-primary" }
    };
    function vwGuessConfirmVariant(btn) {
        var explicit = btn.getAttribute("data-confirm-variant");
        if (explicit && VW_DIALOG_VARIANTS[explicit]) return explicit;
        if (btn.hasAttribute("data-confirm-danger")) return "danger";
        var title = btn.getAttribute("data-confirm-title") || "";
        if (/delete|remove|revoke|deactivate/i.test(title)) return "danger";
        if (/disable|cancel|reject|decline|discard|missed/i.test(title)) return "warning";
        return "default";
    }
    function vwSetDialogIcon(badgeEl, variantKey) {
        var v = VW_DIALOG_VARIANTS[variantKey] || VW_DIALOG_VARIANTS["default"];
        badgeEl.className = "ui-modal-icon " + v.badgeClass;
        badgeEl.innerHTML = '<i data-lucide="' + v.icon + '" class="h-5 w-5"></i>';
        renderIcons();
        return v;
    }

    /* Shared open/close so every modal — the confirm dialog, the alert
       dialog, and any data-modal-open sheet — locks page scroll while
       it's up and restores it once the last open modal closes, and so
       focus lands somewhere useful on open and returns to the trigger
       on close. */
    function vwOpenModal(modal, triggerEl) {
        if (!modal) return;
        modal._vwTrigger = triggerEl || null;
        modal.classList.remove("hidden");
        document.documentElement.classList.add("overflow-hidden");
        var panel = modal.querySelector(".ui-modal-panel");
        if (panel) { panel.setAttribute("tabindex", "-1"); panel.focus({ preventScroll: true }); }
    }
    function vwCloseModal(modal) {
        if (!modal) return;
        modal.classList.add("hidden");
        if (!document.querySelector(".ui-modal:not(.hidden)")) {
            document.documentElement.classList.remove("overflow-hidden");
        }
        if (modal._vwTrigger) { modal._vwTrigger.focus({ preventScroll: true }); modal._vwTrigger = null; }
    }

    function ensureConfirmModal() {
        var modal = document.getElementById("vwConfirmModal");
        if (modal) return modal;
        modal = document.createElement("div");
        modal.id = "vwConfirmModal";
        modal.className = "ui-modal hidden";
        modal.innerHTML =
            '<div class="ui-modal-backdrop" data-confirm-cancel></div>' +
            '<div class="ui-modal-panel ui-modal-panel-sm" role="alertdialog" aria-modal="true" aria-labelledby="vwConfirmTitle">' +
            '  <div class="ui-modal-grip"></div>' +
            '  <div class="ui-modal-header">' +
            '    <div class="flex items-start gap-3">' +
            '      <div id="vwConfirmIcon" class="ui-modal-icon"></div>' +
            '      <h3 id="vwConfirmTitle" class="ui-modal-title pt-1.5"></h3>' +
            '    </div>' +
            '    <button type="button" class="ui-modal-close" data-confirm-cancel aria-label="Close">' +
            '      <i data-lucide="x" class="h-4 w-4"></i>' +
            '    </button>' +
            '  </div>' +
            '  <div class="ui-modal-body"><p id="vwConfirmMessage"></p></div>' +
            '  <div class="ui-modal-footer">' +
            '    <button type="button" class="ui-btn-secondary" data-confirm-cancel>Cancel</button>' +
            '    <button type="button" class="ui-btn-primary" data-confirm-ok>Confirm</button>' +
            '  </div>' +
            '</div>';
        document.body.appendChild(modal);
        renderIcons();
        return modal;
    }

    function initConfirmDialogs() {
        document.querySelectorAll("[data-confirm-title]").forEach(function (btn) {
            if (btn.dataset.wiredConfirm === "1") return;
            btn.dataset.wiredConfirm = "1";
            btn.addEventListener("click", function (e) {
                e.preventDefault();
                var modal = ensureConfirmModal();
                modal.querySelector("#vwConfirmTitle").textContent = btn.getAttribute("data-confirm-title") || "Are you sure?";
                modal.querySelector("#vwConfirmMessage").textContent = btn.getAttribute("data-confirm-message") || "";
                var variantKey = vwGuessConfirmVariant(btn);
                var variant = vwSetDialogIcon(modal.querySelector("#vwConfirmIcon"), variantKey);
                var okBtn = modal.querySelector("[data-confirm-ok]");
                okBtn.className = variant.btnClass;
                okBtn.textContent = btn.getAttribute("data-confirm-ok-label") || "Confirm";
                vwOpenModal(modal, btn);

                function close() { vwCloseModal(modal); cleanup(); }
                function confirm() { close(); btn.closest("form")?.requestSubmit(btn); }
                function cleanup() {
                    modal.querySelectorAll("[data-confirm-cancel]").forEach(function (c) { c.removeEventListener("click", close); });
                    modal.querySelector("[data-confirm-ok]").removeEventListener("click", confirm);
                }
                modal.querySelectorAll("[data-confirm-cancel]").forEach(function (c) { c.addEventListener("click", close); });
                modal.querySelector("[data-confirm-ok]").addEventListener("click", confirm);
            });
        });
    }
    window.vwInitConfirmDialogs = initConfirmDialogs;

    /* ---------------------------------------------------------------- */
    /* One-button alert / acknowledgement dialog                         */
    /* -----------------------------------------------------------------
       For "here's the outcome" moments that deserve more presence than
       a toast — e.g. a success recap with details, or a warning someone
       must actively dismiss. Declarative: data-alert-title (+ optional
       data-alert-message / data-alert-variant / data-alert-ok-label) on
       any button pops it on click. Programmatic: window.vwAlert({title,
       message, variant, okLabel}). Doesn't touch forms or the existing
       TempData -> toast flash-message pipeline; purely additive. */
    function ensureAlertModal() {
        var modal = document.getElementById("vwAlertModal");
        if (modal) return modal;
        modal = document.createElement("div");
        modal.id = "vwAlertModal";
        modal.className = "ui-modal hidden";
        modal.innerHTML =
            '<div class="ui-modal-backdrop" data-alert-dismiss></div>' +
            '<div class="ui-modal-panel ui-modal-panel-sm" role="alertdialog" aria-modal="true" aria-labelledby="vwAlertTitle">' +
            '  <div class="ui-modal-grip"></div>' +
            '  <div class="ui-modal-header">' +
            '    <div class="flex items-start gap-3">' +
            '      <div id="vwAlertIcon" class="ui-modal-icon"></div>' +
            '      <h3 id="vwAlertTitle" class="ui-modal-title pt-1.5"></h3>' +
            '    </div>' +
            '    <button type="button" class="ui-modal-close" data-alert-dismiss aria-label="Close">' +
            '      <i data-lucide="x" class="h-4 w-4"></i>' +
            '    </button>' +
            '  </div>' +
            '  <div class="ui-modal-body"><p id="vwAlertMessage"></p></div>' +
            '  <div class="ui-modal-footer">' +
            '    <button type="button" class="ui-btn-primary" data-alert-dismiss data-alert-ok>OK</button>' +
            '  </div>' +
            '</div>';
        document.body.appendChild(modal);
        renderIcons();
        return modal;
    }

    function vwShowAlert(opts) {
        opts = opts || {};
        var modal = ensureAlertModal();
        modal.querySelector("#vwAlertTitle").textContent = opts.title || "Done";
        modal.querySelector("#vwAlertMessage").textContent = opts.message || "";
        var variant = vwSetDialogIcon(modal.querySelector("#vwAlertIcon"), opts.variant || "success");
        var okBtn = modal.querySelector("[data-alert-ok]");
        if (okBtn) {
            okBtn.className = variant.btnClass;
            okBtn.textContent = opts.okLabel || "OK";
        }
        vwOpenModal(modal, opts.triggerEl || document.activeElement);

        function close() { vwCloseModal(modal); cleanup(); }
        function cleanup() {
            modal.querySelectorAll("[data-alert-dismiss]").forEach(function (c) { c.removeEventListener("click", close); });
        }
        modal.querySelectorAll("[data-alert-dismiss]").forEach(function (c) { c.addEventListener("click", close); });
        return modal;
    }
    window.vwAlert = vwShowAlert;

    function initAlertDialogs() {
        document.querySelectorAll("[data-alert-title]").forEach(function (btn) {
            if (btn.dataset.wiredAlert === "1") return;
            btn.dataset.wiredAlert = "1";
            btn.addEventListener("click", function (e) {
                e.preventDefault();
                vwShowAlert({
                    title: btn.getAttribute("data-alert-title"),
                    message: btn.getAttribute("data-alert-message") || "",
                    variant: btn.getAttribute("data-alert-variant") || "success",
                    okLabel: btn.getAttribute("data-alert-ok-label"),
                    triggerEl: btn
                });
            });
        });
    }
    window.vwInitAlertDialogs = initAlertDialogs;

    /* ---------------------------------------------------------------- */
    /* Inline alert banners (.ui-alert-success/-danger/-warning/-info)    */
    /* -----------------------------------------------------------------
       These are static server-rendered banners (validation summaries,
       flash messages, page notices) — not the toast or the modal dialog.
       This just wires two small, optional enhancements on top of markup
       that already exists:
         1. If a banner didn't include an icon, inject the right default
            for its variant so every alert gets the icon-badge treatment
            without every call site having to hand-roll it.
         2. If a banner opts in with data-dismissible, attach a close
            button that animates it away on click.
       Purely additive — a banner with no icon and no data-dismissible
       renders exactly as it did before. */
    var VW_ALERT_ICONS = { success: "check-circle", danger: "alert-circle", warning: "alert-triangle", info: "info" };

    function vwAlertVariant(el) {
        for (var key in VW_ALERT_ICONS) {
            if (el.classList.contains("ui-alert-" + key)) return key;
        }
        return null;
    }

    function dismissInlineAlert(el) {
        if (!el || el._vwDismissing) return;
        el._vwDismissing = true;
        el.classList.add("is-dismissing");
        el.addEventListener("animationend", function () { el.remove(); }, { once: true });
        setTimeout(function () { el.remove(); }, 200);
    }

    function initInlineAlerts() {
        document.querySelectorAll(".ui-alert-success, .ui-alert-danger, .ui-alert-warning, .ui-alert-info").forEach(function (el) {
            if (el.dataset.wiredAlertBanner === "1") return;
            el.dataset.wiredAlertBanner = "1";

            var variant = vwAlertVariant(el);
            if (variant && !el.querySelector(".ui-alert-icon")) {
                var badge = document.createElement("div");
                badge.className = "ui-alert-icon";
                badge.innerHTML = '<i data-lucide="' + VW_ALERT_ICONS[variant] + '" class="h-4 w-4"></i>';
                el.insertBefore(badge, el.firstChild);
            }

            if (el.hasAttribute("data-dismissible") && !el.querySelector(".ui-alert-close")) {
                var closeBtn = document.createElement("button");
                closeBtn.type = "button";
                closeBtn.className = "ui-alert-close";
                closeBtn.setAttribute("aria-label", "Dismiss notification");
                closeBtn.innerHTML = '<i data-lucide="x" class="h-4 w-4"></i>';
                closeBtn.addEventListener("click", function () { dismissInlineAlert(el); });
                el.appendChild(closeBtn);
            }
        });
        renderIcons();
    }
    window.vwInitInlineAlerts = initInlineAlerts;

    /* ---------------------------------------------------------------- */
    /* Full-screen loading overlay                                        */
    /* -----------------------------------------------------------------
       For actions with nothing else on screen to show progress while
       they run — most commonly a form submit that navigates away (exam
       submission, starting an assessment) where the page would otherwise
       just sit there looking frozen for a moment. One overlay element is
       created lazily and reused; calling vwShowOverlay again just updates
       the message instead of stacking a second one.
       Programmatic: window.vwShowOverlay("Submitting…"), vwHideOverlay().
       Declarative: add data-loading-overlay="Message" to a <form> and the
       existing submit-time handler below shows it automatically — no
       separate wiring needed per form. It's never auto-hidden on a
       timer, since the whole point is it should last until the page
       actually navigates; call vwHideOverlay() explicitly if a submit
       can fail without a navigation (e.g. an ajax call). */
    function ensureLoadingOverlay() {
        var overlay = document.getElementById("vwLoadingOverlay");
        if (overlay) return overlay;
        overlay = document.createElement("div");
        overlay.id = "vwLoadingOverlay";
        overlay.className = "ui-loading-overlay hidden";
        overlay.setAttribute("role", "status");
        overlay.setAttribute("aria-live", "polite");
        overlay.innerHTML =
            '<span class="ui-loader-lg"></span>' +
            '<p class="ui-loading-overlay-message"></p>';
        document.body.appendChild(overlay);
        return overlay;
    }
    function vwShowOverlay(message) {
        var overlay = ensureLoadingOverlay();
        overlay.querySelector(".ui-loading-overlay-message").textContent = message || "Please wait…";
        overlay.classList.remove("hidden");
        document.documentElement.classList.add("overflow-hidden");
    }
    function vwHideOverlay() {
        var overlay = document.getElementById("vwLoadingOverlay");
        if (!overlay) return;
        overlay.classList.add("hidden");
        if (!document.querySelector(".ui-modal:not(.hidden)")) {
            document.documentElement.classList.remove("overflow-hidden");
        }
    }
    window.vwShowOverlay = vwShowOverlay;
    window.vwHideOverlay = vwHideOverlay;

    /* ---------------------------------------------------------------- */
    /* Submit-button loading state                                        */
    /* -----------------------------------------------------------------
       Declarative, same spirit as the loading overlay above: any
       <form>'s submit shows a spinner (.is-loading, see tailwind-input
       .css) on whichever button triggered it — no per-form wiring
       needed anywhere. Opt out per-button with data-no-loading-state
       (e.g. a button that opens a confirm dialog first rather than
       submitting immediately, or one that intentionally stays enabled
       for a double-submit-safe flow that isn't a real navigation).

       Runs only once the browser's own constraint validation has
       already passed (checkValidity()), so a required field left
       blank doesn't get a permanently-spinning, disabled button for a
       form that was never actually going to submit. That check covers
       native HTML5 rules; a handful of jQuery Unobtrusive Validation's
       custom rules (remote/compare) live outside that API, which is
       exactly why the safety-net timeout below exists too — belt and
       suspenders, since this can't be interactively tested here. */
    document.addEventListener("submit", function (e) {
        var form = e.target;
        if (!(form instanceof HTMLFormElement)) return;
        if (form.hasAttribute("data-no-loading-state")) return;
        if (typeof form.checkValidity === "function" && !form.checkValidity()) return;
        var btn = e.submitter || form.querySelector('button[type="submit"]');
        if (!btn || btn.hasAttribute("data-no-loading-state") || btn.classList.contains("is-loading")) return;
        btn.classList.add("is-loading");
        // IMPORTANT: btn.disabled must NOT be set synchronously here. Browsers finalize
        // which fields belong to the submission (including the clicked submit button's
        // own name/value pair, e.g. name="status" value="Verified") at the moment this
        // submit event finishes dispatching. A disabled form control is excluded from
        // that data set — so disabling the button on the spot silently strips its
        // name/value from the POST body while the rest of the form still submits
        // normally, with no visible error. Deferring by a tick lets the browser capture
        // the submission first; the button is still visually disabled well before the
        // page can respond to anything.
        setTimeout(function () {
            btn.disabled = true;
        }, 0);
        // Safety net: if the page hasn't navigated away in 10s (a
        // submit that fails silently, or a validation path
        // checkValidity() doesn't cover), don't leave it stuck forever.
        setTimeout(function () {
            btn.classList.remove("is-loading");
            btn.disabled = false;
        }, 10000);
    }, true);

    /* ---------------------------------------------------------------- */
    /* Skeleton loaders                                                     */
    /* -----------------------------------------------------------------
       Generalizes the "brief, deliberate loading state" already used for
       the Admin dashboard's stat cards into a declarative pairing any
       view can opt into for a table, card grid, form, or list — no
       bespoke toggle script needed per page:
         data-skeleton="group"          on the placeholder (built from
                                         .ui-skeleton blocks shaped like
                                         the real content — rows, cards,
                                         fields, list items, etc.)
         data-skeleton-content="group"  on the real content, which starts
                                         with the "hidden" class
         data-skeleton-delay="300"      optional, ms, on the placeholder
                                         (defaults to 300 — same delay the
                                         stat cards have always used)
       All placeholder/content pairs sharing a group are revealed
       together once that group's delay elapses. Reduced-motion users get
       an instant swap with no artificial delay and no fade. Revealing a
       group also fires a "vw:skeleton-revealed" CustomEvent (detail:
       { group }) on document, so page-specific follow-up work — restart
       a stat counter, re-render icons that were sitting in hidden markup
       — can hook in without this helper needing to know about any of it.
       Content that only becomes ready via a real async event (e.g. the
       dashboard chart waiting on Chart.js) should keep doing its own
       thing and call vwRevealSkeletonGroup directly instead of relying
       on the timer. */
    function revealSkeletonGroup(group) {
        document.querySelectorAll('[data-skeleton="' + group + '"]').forEach(function (el) {
            el.classList.add("hidden");
        });
        document.querySelectorAll('[data-skeleton-content="' + group + '"]').forEach(function (el) {
            el.classList.remove("hidden");
            el.classList.add("animate-fade-in");
        });
        renderIcons();
        document.dispatchEvent(new CustomEvent("vw:skeleton-revealed", { detail: { group: group } }));
    }
    function initSkeletons() {
        var reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
        var seen = {};
        document.querySelectorAll("[data-skeleton]").forEach(function (el) {
            var group = el.getAttribute("data-skeleton");
            if (!group || seen[group]) return;
            seen[group] = true;
            var delay = parseInt(el.getAttribute("data-skeleton-delay"), 10);
            if (isNaN(delay)) delay = 300;
            setTimeout(function () { revealSkeletonGroup(group); }, reduceMotion ? 0 : delay);
        });
    }
    window.vwInitSkeletons = initSkeletons;
    window.vwRevealSkeletonGroup = revealSkeletonGroup;

    /* ---------------------------------------------------------------- */
    /* Generic modal helper: data-modal-open="id" / data-modal-close      */
    /* Clicking the backdrop itself also closes, same as Escape.          */
    /* ---------------------------------------------------------------- */
    function initModals() {
        document.querySelectorAll("[data-modal-open]").forEach(function (btn) {
            btn.addEventListener("click", function () {
                vwOpenModal(document.getElementById(btn.getAttribute("data-modal-open")), btn);
            });
        });
        document.querySelectorAll("[data-modal-close]").forEach(function (btn) {
            btn.addEventListener("click", function () { vwCloseModal(btn.closest(".ui-modal")); });
        });
        document.querySelectorAll(".ui-modal-backdrop").forEach(function (backdrop) {
            if (backdrop.hasAttribute("data-confirm-cancel")) return; // handled by its own dialog
            backdrop.addEventListener("click", function () { vwCloseModal(backdrop.closest(".ui-modal")); });
        });
        document.addEventListener("keydown", function (e) {
            if (e.key !== "Escape") return;
            document.querySelectorAll(".ui-modal:not(.hidden)").forEach(function (m) { vwCloseModal(m); });
        });
    }

    /* ---------------------------------------------------------------- */
    /* Tiny accordion helper — data-accordion-toggle / data-accordion-body */
    /* (used by Views/Shared/EditorTemplates/EducationLevelFieldset.cshtml */
    /* now that Bootstrap's JS collapse is no longer assumed available)   */
    /* ---------------------------------------------------------------- */
    function initAccordions() {
        document.querySelectorAll("[data-accordion-toggle]").forEach(function (trigger) {
            if (trigger.dataset.wiredAccordion === "1") return;
            trigger.dataset.wiredAccordion = "1";
            trigger.addEventListener("click", function () {
                var body = document.getElementById(trigger.getAttribute("data-accordion-toggle"));
                if (!body) return;
                var isOpen = trigger.getAttribute("aria-expanded") === "true";
                trigger.setAttribute("aria-expanded", isOpen ? "false" : "true");
                body.classList.toggle("hidden", isOpen);
                trigger.classList.toggle("is-collapsed", isOpen);
            });
        });
    }
    window.vwInitAccordions = initAccordions;

    /* ---------------------------------------------------------------- */
    /* Generic tab component — data-tabs-trigger="panelId" (in a group    */
    /* wrapped by [data-tabs]) / data-tabs-panel="panelId".               */
    /* Vanilla replacement for Bootstrap's nav-pills + data-bs-toggle so   */
    /* fully-modernized views (e.g. Admin dashboard) don't need Bootstrap  */
    /* JS. Persists the active tab per group in sessionStorage so a form   */
    /* postback (e.g. updating a setting) reopens on the same tab.        */
    /* ---------------------------------------------------------------- */
    function activateTab(group, panelId, urlMode) {
        var groupEl = document.querySelector('[data-tabs="' + group + '"]');
        if (!groupEl) return;
        if (!groupEl.querySelector('[data-tabs-trigger="' + panelId + '"]')) return;
        groupEl.querySelectorAll("[data-tabs-trigger]").forEach(function (t) {
            var isActive = t.getAttribute("data-tabs-trigger") === panelId;
            t.classList.toggle("is-active", isActive);
            t.setAttribute("aria-selected", isActive ? "true" : "false");
        });
        document.querySelectorAll('[data-tabs-panel][data-tabs-group="' + group + '"]').forEach(function (p) {
            var show = p.getAttribute("data-tabs-panel") === panelId;
            p.classList.toggle("hidden", !show);
            if (show) {
                // Retrigger the fade-in so switching tabs feels like fresh content loading in.
                p.classList.remove("animate-fade-in");
                void p.offsetWidth;
                p.classList.add("animate-fade-in");
            }
        });
        try { sessionStorage.setItem("vw-tab-" + group, panelId); } catch (e) { /* storage unavailable */ }

        // Keep the URL in sync with the active tab (?tab=panelId) so a specific
        // section can be bookmarked or shared, instead of that state living only
        // in sessionStorage where it's invisible outside the current browser tab.
        // urlMode: "push" (user clicked a tab -> new history entry, enables Back/
        // Forward between tabs), "replace" (initial page load -> normalize the
        // URL without adding a history entry), or falsy (triggered by a
        // popstate/Back-Forward navigation -> don't touch history again).
        if (urlMode && window.history && window.URL) {
            try {
                var url = new URL(window.location.href);
                url.searchParams.set("tab", panelId);
                var state = { vwTab: panelId, vwTabGroup: group };
                if (urlMode === "push") {
                    window.history.pushState(state, "", url);
                } else {
                    window.history.replaceState(state, "", url);
                }
            } catch (e) { /* URL/history API unavailable */ }
        }
    }

    function initTabs() {
        document.querySelectorAll("[data-tabs]").forEach(function (groupEl) {
            var group = groupEl.getAttribute("data-tabs");
            var triggers = groupEl.querySelectorAll("[data-tabs-trigger]");
            if (!triggers.length) return;

            // Which tab opens first: an explicit ?tab= in the URL wins (so a
            // shared/bookmarked link always lands on the right section), then
            // whichever tab this browser last had open in this session, then
            // the first tab as the default.
            var fromUrl = null;
            try { fromUrl = new URLSearchParams(window.location.search).get("tab"); } catch (e) { /* ignore */ }
            var hasFromUrl = fromUrl && groupEl.querySelector('[data-tabs-trigger="' + fromUrl + '"]');

            var stored = null;
            try { stored = sessionStorage.getItem("vw-tab-" + group); } catch (e) { /* ignore */ }
            var hasStored = stored && groupEl.querySelector('[data-tabs-trigger="' + stored + '"]');

            var initial = hasFromUrl ? fromUrl : (hasStored ? stored : triggers[0].getAttribute("data-tabs-trigger"));
            // "replace" on load: opening the page shouldn't itself create a new
            // history entry, but the URL should still reflect the active tab.
            activateTab(group, initial, "replace");

            triggers.forEach(function (trigger) {
                trigger.addEventListener("click", function () {
                    activateTab(group, trigger.getAttribute("data-tabs-trigger"), "push");
                });
            });
        });
    }
    window.vwInitTabs = initTabs;

    // Support Back/Forward navigating between tabs once a click has pushed a
    // history entry for them (see urlMode "push" above).
    window.addEventListener("popstate", function (e) {
        var state = e.state;
        if (state && state.vwTabGroup && state.vwTab) {
            activateTab(state.vwTabGroup, state.vwTab, null);
        }
    });
    window.vwActivateTab = activateTab;

    /* ---------------------------------------------------------------- */
    /* Stat counter animation — data-count-to="123" on the element that   */
    /* should display the number. Purely cosmetic (numbers are rendered   */
    /* server-side already); gives dashboards a lightweight loading feel  */
    /* without needing real async fetches.                               */
    /* ---------------------------------------------------------------- */
    function initStatCounters() {
        var els = document.querySelectorAll("[data-count-to]");
        if (!els.length) return;
        var reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
        els.forEach(function (el) {
            var target = parseFloat(el.getAttribute("data-count-to"));
            if (isNaN(target)) return;
            var decimals = el.getAttribute("data-count-decimals") ? parseInt(el.getAttribute("data-count-decimals"), 10) : 0;
            var suffix = el.getAttribute("data-count-suffix") || "";
            if (reduceMotion) { el.textContent = target.toFixed(decimals) + suffix; return; }

            var duration = 700, start = null;
            function frame(ts) {
                if (start === null) start = ts;
                var progress = Math.min((ts - start) / duration, 1);
                var eased = 1 - Math.pow(1 - progress, 3);
                el.textContent = (target * eased).toFixed(decimals) + suffix;
                if (progress < 1) requestAnimationFrame(frame);
            }
            requestAnimationFrame(frame);
        });
    }
    window.vwInitStatCounters = initStatCounters;

    /* ---------------------------------------------------------------- */
    /* Password fields — visibility toggle, strength meter, confirm-      */
    /* match hint. One implementation shared by every password field      */
    /* site-wide (Login, Register, ResetPassword, AssessmentAuth, the     */
    /* Admin "create staff user" temp password), wired entirely through   */
    /* data-pw-* attributes so page markup stays declarative:             */
    /*   data-pw-toggle="<input id>"        on the eye/eye-off button     */
    /*   data-pw-strength="<meter id>"      on the password <input>       */
    /*   data-pw-match="<other input id>"   on a confirm-password <input> */
    /*   data-pw-match-hint="<hint id>"     on that same confirm <input>  */
    /* The toggle button's icon is always a Lucide <i data-lucide> now      */
    /* (Font Awesome has been removed app-wide); the classList branch below */
    /* is kept only as a harmless fallback in case any markup still uses    */
    /* the old fa-eye/fa-eye-slash classes. */
    /* ---------------------------------------------------------------- */
    function togglePasswordVisibility(btn) {
        var input = document.getElementById(btn.getAttribute("data-pw-toggle"));
        if (!input) return;
        var willShow = input.type === "password";
        input.type = willShow ? "text" : "password";
        btn.setAttribute("aria-label", willShow ? "Hide password" : "Show password");
        btn.setAttribute("aria-pressed", willShow ? "true" : "false");

        var icon = btn.querySelector("i");
        if (!icon) return;
        if (icon.hasAttribute("data-lucide")) {
            icon.setAttribute("data-lucide", willShow ? "eye-off" : "eye");
            renderIcons();
        } else if (icon.classList.contains("fa-eye") || icon.classList.contains("fa-eye-slash")) {
            icon.classList.toggle("fa-eye", !willShow);
            icon.classList.toggle("fa-eye-slash", willShow);
        }
    }

    function initPasswordToggles() {
        document.querySelectorAll("[data-pw-toggle]").forEach(function (btn) {
            if (btn.dataset.wiredPwToggle === "1") return;
            btn.dataset.wiredPwToggle = "1";
            btn.setAttribute("aria-pressed", "false");
            btn.addEventListener("click", function () { togglePasswordVisibility(btn); });
        });
    }

    /* UI-only strength signal (length + character variety). This never
       blocks submission and duplicates nothing the server already
       validates — it just gives the person feedback as they type. */
    var PW_STRENGTH_LEVELS = [
        { level: "weak", label: "Weak" },
        { level: "fair", label: "Fair" },
        { level: "good", label: "Good" },
        { level: "strong", label: "Strong" }
    ];

    function scorePasswordValue(value) {
        var score = 0;
        if (!value) return score;
        if (value.length >= 8) score++;
        if (value.length >= 12) score++;
        if (/[a-z]/.test(value) && /[A-Z]/.test(value)) score++;
        if (/\d/.test(value)) score++;
        if (/[^A-Za-z0-9]/.test(value)) score++;
        return score;
    }

    function updateStrengthMeter(meter, value) {
        var segs = meter.querySelectorAll(".pw-strength-seg");
        var labelEl = meter.querySelector(".pw-strength-label");
        if (!value) {
            meter.removeAttribute("data-level");
            segs.forEach(function (s) { s.classList.remove("is-filled"); });
            if (labelEl) labelEl.textContent = "";
            return;
        }
        var score = scorePasswordValue(value);
        var levelIndex = score <= 1 ? 0 : score === 2 ? 1 : score === 3 ? 2 : 3;
        var info = PW_STRENGTH_LEVELS[levelIndex];
        meter.setAttribute("data-level", info.level);
        segs.forEach(function (s, i) { s.classList.toggle("is-filled", i <= levelIndex); });
        if (labelEl) labelEl.textContent = info.label;
    }

    function initPasswordStrength() {
        document.querySelectorAll("[data-pw-strength]").forEach(function (input) {
            if (input.dataset.wiredPwStrength === "1") return;
            input.dataset.wiredPwStrength = "1";
            var meter = document.getElementById(input.getAttribute("data-pw-strength"));
            if (!meter) return;
            input.addEventListener("input", function () { updateStrengthMeter(meter, input.value); });
            updateStrengthMeter(meter, input.value);
        });
    }

    function initPasswordMatch() {
        document.querySelectorAll("[data-pw-match]").forEach(function (input) {
            if (input.dataset.wiredPwMatch === "1") return;
            input.dataset.wiredPwMatch = "1";
            var other = document.getElementById(input.getAttribute("data-pw-match"));
            var hint = document.getElementById(input.getAttribute("data-pw-match-hint"));
            if (!other || !hint) return;

            function check() {
                if (!input.value) {
                    hint.classList.remove("is-visible", "is-match", "is-mismatch");
                    return;
                }
                var matches = input.value === other.value;
                hint.classList.add("is-visible");
                hint.classList.toggle("is-match", matches);
                hint.classList.toggle("is-mismatch", !matches);
                hint.textContent = matches ? "✓ Passwords match" : "✕ Passwords don't match";
            }
            input.addEventListener("input", check);
            other.addEventListener("input", check);
        });
    }

    function initPasswordEnhancements() {
        initPasswordToggles();
        initPasswordStrength();
        initPasswordMatch();
    }
    window.vwInitPasswordEnhancements = initPasswordEnhancements;

    /* ---------------------------------------------------------------- */
    /* Registration field validation — data-live-validate="fullname|      */
    /* email|phone" on Full Name / Email / Phone Number inputs on the     */
    /* Register page.                                                     */
    /* -----------------------------------------------------------------
       UX-only, client-side mirror of the authoritative rules in
       Services/RegistrationValidation.cs — never treat this as the real
       check; the server re-validates everything on submit regardless.
       Deliberately light-touch per the "don't be annoying while typing"
       requirement: fields validate on blur, errors clear as soon as the
       person starts correcting them (on input), and nothing runs before
       the first blur. Reuses each field's existing server-rendered
       asp-validation-for <span> (found via aria-describedby) instead of
       creating new DOM, so this works whether the message on screen came
       from the server (after a full submit) or this client-side check. */
    /* ---------------------------------------------------------------- */
    var FULLNAME_PATTERN = /^\p{L}[\p{L}\s.'-]*[\p{L}.]$/u;
    var FULLNAME_REPEATED_PUNCT = /[\s.'-]{3,}/;
    var EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;
    var PHONE_PATTERN = /^[6-9]\d{9}$/;

    function collapseSpaces(value) { return value.trim().replace(/ {2,}/g, " "); }

    function isValidFullNameClientSide(value) {
        if (!value) return false;
        if (!FULLNAME_PATTERN.test(value)) return false;
        if (FULLNAME_REPEATED_PUNCT.test(value)) return false;
        var letters = (value.match(/\p{L}/gu) || []).length;
        return letters >= 2;
    }

    function normalizePhoneClientSide(value) {
        var digits = (value || "").replace(/\D/g, "");
        if (digits.length === 12 && digits.indexOf("91") === 0) digits = digits.slice(2);
        else if (digits.length === 11 && digits.indexOf("0") === 0) digits = digits.slice(1);
        return digits;
    }

    function isValidPhoneClientSide(digits) {
        if (!PHONE_PATTERN.test(digits)) return false;
        return digits.split("").some(function (d) { return d !== digits[0]; });
    }

    function fieldErrorEl(input) {
        var describedBy = input.getAttribute("aria-describedby");
        if (!describedBy) return null;
        var id = describedBy.split(/\s+/)[0];
        return document.getElementById(id);
    }

    function showFieldError(input, message) {
        input.classList.toggle("is-invalid", !!message);
        var errEl = fieldErrorEl(input);
        if (errEl) errEl.textContent = message || "";
    }

    function initLiveFieldValidation() {
        document.querySelectorAll('[data-live-validate="fullname"]').forEach(function (input) {
            if (input.dataset.wiredLiveValidate === "1") return;
            input.dataset.wiredLiveValidate = "1";
            input.addEventListener("blur", function () {
                var normalized = collapseSpaces(input.value);
                input.value = normalized;
                showFieldError(input, normalized && !isValidFullNameClientSide(normalized)
                    ? "Enter a valid full name (letters, spaces, hyphens, and apostrophes only)."
                    : "");
            });
            input.addEventListener("input", function () {
                if (input.classList.contains("is-invalid")) showFieldError(input, "");
            });
        });

        document.querySelectorAll('[data-live-validate="email"]').forEach(function (input) {
            if (input.dataset.wiredLiveValidate === "1") return;
            input.dataset.wiredLiveValidate = "1";
            input.addEventListener("blur", function () {
                var value = input.value.trim();
                showFieldError(input, value && !EMAIL_PATTERN.test(value) ? "Enter a valid email address." : "");
            });
            input.addEventListener("input", function () {
                if (input.classList.contains("is-invalid")) showFieldError(input, "");
            });
        });

        document.querySelectorAll('[data-live-validate="phone"]').forEach(function (input) {
            if (input.dataset.wiredLiveValidate === "1") return;
            input.dataset.wiredLiveValidate = "1";
            input.addEventListener("blur", function () {
                var digits = normalizePhoneClientSide(input.value);
                showFieldError(input, digits && !isValidPhoneClientSide(digits)
                    ? "Enter a valid 10-digit Indian mobile number."
                    : "");
            });
            input.addEventListener("input", function () {
                if (input.classList.contains("is-invalid")) showFieldError(input, "");
            });
        });
    }
    window.vwInitLiveFieldValidation = initLiveFieldValidation;

    /* ---------------------------------------------------------------- */
    /* Searchable combobox — data-searchable="true" on any <select>.      */
    /* -----------------------------------------------------------------
       Progressively enhances a plain <select> into a filterable
       combobox: the original <select> stays in the DOM (same name/id/
       asp-for binding, so posted data and any existing validation are
       completely unaffected) but is visually hidden; a button + search
       panel drive it and keep it in sync, dispatching a real "change"
       event on every update so other listeners (e.g. dependent fields)
       keep working exactly as before. Disabled selects are respected
       and rendered as a disabled combobox instead of being skipped. */
    /* ---------------------------------------------------------------- */
    var comboIdSeed = 0;

    function buildComboOptionsFromSelect(select) {
        return Array.prototype.map.call(select.options, function (opt) {
            return { value: opt.value, label: opt.textContent, disabled: opt.disabled };
        });
    }

    function closeAllCombos(except) {
        document.querySelectorAll(".vw-combo.is-open").forEach(function (c) {
            if (c === except) return;
            c.classList.remove("is-open");
            var panel = c.querySelector(".vw-combo-panel");
            if (panel) panel.hidden = true;
            var control = c.querySelector(".vw-combo-control");
            if (control) control.setAttribute("aria-expanded", "false");
        });
    }

    function enhanceSearchableSelect(select) {
        if (select.dataset.wiredCombo === "1") return;
        select.dataset.wiredCombo = "1";
        comboIdSeed++;

        var wrap = document.createElement("div");
        wrap.className = "vw-combo";

        var control = document.createElement("button");
        control.type = "button";
        control.className = "vw-combo-control";
        control.setAttribute("aria-haspopup", "listbox");
        control.setAttribute("aria-expanded", "false");
        var panelId = "vwComboPanel" + comboIdSeed;
        control.setAttribute("aria-controls", panelId);

        var valueEl = document.createElement("span");
        valueEl.className = "vw-combo-value";

        var chevron = document.createElement("i");
        chevron.setAttribute("data-lucide", "chevron-down");
        chevron.className = "vw-combo-chevron";

        control.appendChild(valueEl);
        control.appendChild(chevron);

        var panel = document.createElement("div");
        panel.className = "vw-combo-panel";
        panel.id = panelId;
        panel.hidden = true;

        var searchWrap = document.createElement("div");
        searchWrap.className = "vw-combo-search-wrap";
        searchWrap.innerHTML = '<i data-lucide="search"></i>';
        var search = document.createElement("input");
        search.type = "text";
        search.className = "vw-combo-search";
        search.setAttribute("placeholder", select.getAttribute("data-searchable-placeholder") || "Search…");
        search.setAttribute("aria-label", "Search options");
        searchWrap.appendChild(search);

        var optionsList = document.createElement("ul");
        optionsList.className = "vw-combo-options";
        optionsList.setAttribute("role", "listbox");

        panel.appendChild(searchWrap);
        panel.appendChild(optionsList);

        select.classList.add("vw-combo-native");
        select.parentNode.insertBefore(wrap, select);
        wrap.appendChild(select);
        wrap.appendChild(control);
        wrap.appendChild(panel);

        var allOptions = [];

        function refreshValue() {
            var opt = select.options[select.selectedIndex];
            var hasValue = opt && opt.value !== "";
            valueEl.textContent = hasValue ? opt.textContent : (select.getAttribute("data-searchable-placeholder-text") || (opt ? opt.textContent : "-- Select --"));
            valueEl.classList.toggle("is-placeholder", !hasValue);
        }

        function refreshDisabled() {
            var disabled = select.disabled;
            control.classList.toggle("is-disabled", disabled);
            control.disabled = disabled;
            control.setAttribute("aria-disabled", disabled ? "true" : "false");
        }

        function renderOptions(filterText) {
            allOptions = buildComboOptionsFromSelect(select);
            var q = (filterText || "").trim().toLowerCase();
            var filtered = q ? allOptions.filter(function (o) { return o.label.toLowerCase().indexOf(q) !== -1; }) : allOptions;

            optionsList.innerHTML = "";
            if (!filtered.length) {
                var empty = document.createElement("li");
                empty.className = "vw-combo-empty";
                empty.textContent = "No matches found";
                optionsList.appendChild(empty);
                return;
            }
            filtered.forEach(function (o) {
                var li = document.createElement("li");
                li.className = "vw-combo-option" + (o.value === select.value ? " is-selected" : "");
                li.setAttribute("role", "option");
                li.setAttribute("aria-selected", o.value === select.value ? "true" : "false");
                li.dataset.value = o.value;
                if (o.disabled) li.classList.add("is-disabled");

                var label = document.createElement("span");
                label.textContent = o.label;
                li.appendChild(label);
                if (o.value === select.value) {
                    li.insertAdjacentHTML("beforeend", '<i data-lucide="check"></i>');
                }
                if (!o.disabled) {
                    li.addEventListener("click", function () {
                        select.value = o.value;
                        select.dispatchEvent(new Event("change", { bubbles: true }));
                        refreshValue();
                        close();
                        control.focus();
                    });
                }
                optionsList.appendChild(li);
            });
            renderIcons();
        }

        function open() {
            if (select.disabled) return;
            closeAllCombos(wrap);
            wrap.classList.add("is-open");
            panel.hidden = false;
            control.setAttribute("aria-expanded", "true");
            search.value = "";
            renderOptions("");
            setTimeout(function () { search.focus(); }, 0);
        }
        function close() {
            wrap.classList.remove("is-open");
            panel.hidden = true;
            control.setAttribute("aria-expanded", "false");
        }

        control.addEventListener("click", function () {
            if (wrap.classList.contains("is-open")) close(); else open();
        });
        search.addEventListener("input", function () { renderOptions(search.value); });
        control.addEventListener("keydown", function (e) {
            if (e.key === "ArrowDown" || e.key === "Enter" || e.key === " ") { e.preventDefault(); open(); }
        });
        panel.addEventListener("keydown", function (e) {
            if (e.key === "Escape") { e.preventDefault(); close(); control.focus(); }
            if (e.key === "Enter") {
                e.preventDefault();
                var active = optionsList.querySelector(".vw-combo-option:not(.is-disabled)");
                if (active) active.click();
            }
        });
        document.addEventListener("click", function (e) {
            if (!wrap.contains(e.target)) close();
        });

        // Keep the combobox correct if something else (or a page's own
        // script, e.g. cascading dropdown logic) changes the native
        // select directly instead of going through the combobox UI.
        select.addEventListener("change", refreshValue);
        var observer = new MutationObserver(function () { refreshDisabled(); refreshValue(); });
        observer.observe(select, { attributes: true, attributeFilter: ["disabled"] });

        refreshValue();
        refreshDisabled();
    }

    function initSearchableSelects() {
        document.querySelectorAll("select[data-searchable]").forEach(enhanceSearchableSelect);
    }
    window.vwInitSearchableSelects = initSearchableSelects;

    /* ---------------------------------------------------------------- */
    /* Multi-select chip control — data-msel driven.                     */
    /* -----------------------------------------------------------------
       Usage:
         <div class="vw-msel" data-msel data-msel-target="#hiddenInputId"
              data-msel-placeholder="Select…" data-msel-join=", ">
           <!-- options, one per value, disabled ones get data-disabled -->
           <template data-msel-option value="Bengaluru">Bengaluru</template>
           ...
         </div>
       On load, reads the target hidden input's current value (split on
       data-msel-join, default ", ") to pre-check matching options, then
       keeps that same hidden input updated as chips are added/removed —
       so whatever field already consumes that hidden input (a model
       string field, an existing JS handler, etc.) keeps working exactly
       as before; only the picking experience changes. */
    /* ---------------------------------------------------------------- */
    var mselIdSeed = 0;

    function enhanceMultiSelect(root) {
        if (root.dataset.wiredMsel === "1") return;
        root.dataset.wiredMsel = "1";
        mselIdSeed++;

        var targetSel = root.getAttribute("data-msel-target");
        var target = targetSel ? document.querySelector(targetSel) : null;
        if (!target) return;

        var joiner = root.getAttribute("data-msel-join") || ", ";
        var placeholderText = root.getAttribute("data-msel-placeholder") || "Select options…";
        var isDisabled = root.hasAttribute("data-msel-disabled");

        var options = Array.prototype.map.call(root.querySelectorAll("[data-msel-option]"), function (tpl) {
            // NOTE: tpl is a <template> element — its markup is parsed into the
            // separate, detached tpl.content DocumentFragment, not into tpl's own
            // childNodes. Reading tpl.textContent here always returns "" (which is
            // why option labels rendered blank in the multi-select panel);
            // tpl.content.textContent is the correct way to read the option's label.
            var label = tpl.content.textContent.trim();
            return { value: tpl.getAttribute("value") || label, label: label, disabled: tpl.hasAttribute("data-disabled") };
        });
        root.querySelectorAll("[data-msel-option]").forEach(function (n) { n.remove(); });

        var selected = (target.value || "").split(joiner.trim() === "," ? "," : joiner)
            .map(function (v) { return v.trim(); })
            .filter(Boolean)
            .filter(function (v) { return options.some(function (o) { return o.value === v; }); });

        var control = document.createElement("button");
        control.type = "button";
        control.className = "vw-msel-control";
        control.setAttribute("aria-haspopup", "listbox");
        control.setAttribute("aria-expanded", "false");
        var panelId = "vwMselPanel" + mselIdSeed;
        control.setAttribute("aria-controls", panelId);

        var chipsWrap = document.createElement("span");
        chipsWrap.className = "vw-msel-chips";

        var chevron = document.createElement("i");
        chevron.setAttribute("data-lucide", "chevron-down");
        chevron.className = "vw-msel-chevron";

        control.appendChild(chipsWrap);
        control.appendChild(chevron);

        var panel = document.createElement("div");
        panel.className = "vw-msel-panel";
        panel.id = panelId;
        panel.hidden = true;

        var searchWrap = document.createElement("div");
        searchWrap.className = "vw-msel-search-wrap";
        searchWrap.innerHTML = '<i data-lucide="search"></i>';
        var search = document.createElement("input");
        search.type = "text";
        search.className = "vw-msel-search";
        search.setAttribute("placeholder", "Search…");
        search.setAttribute("aria-label", "Search options");
        searchWrap.appendChild(search);

        var optionsList = document.createElement("ul");
        optionsList.className = "vw-msel-options";
        optionsList.setAttribute("role", "listbox");
        optionsList.setAttribute("aria-multiselectable", "true");

        panel.appendChild(searchWrap);
        panel.appendChild(optionsList);
        root.appendChild(control);
        root.appendChild(panel);

        function syncTarget() {
            target.value = selected.join(joiner);
            target.dispatchEvent(new Event("change", { bubbles: true }));
        }

        function renderChips() {
            chipsWrap.innerHTML = "";
            if (!selected.length) {
                var ph = document.createElement("span");
                ph.className = "vw-msel-placeholder";
                ph.textContent = placeholderText;
                chipsWrap.appendChild(ph);
                return;
            }
            selected.forEach(function (val) {
                var opt = options.find(function (o) { return o.value === val; });
                var chip = document.createElement("span");
                chip.className = "vw-msel-chip";
                var label = document.createElement("span");
                label.textContent = opt ? opt.label : val;
                chip.appendChild(label);
                if (!isDisabled) {
                    var remove = document.createElement("button");
                    remove.type = "button";
                    remove.className = "vw-msel-chip-remove";
                    remove.setAttribute("aria-label", "Remove " + (opt ? opt.label : val));
                    remove.innerHTML = '<i data-lucide="x"></i>';
                    remove.addEventListener("click", function (e) {
                        e.stopPropagation();
                        selected = selected.filter(function (v) { return v !== val; });
                        renderChips(); renderOptions(search.value); syncTarget();
                    });
                    chip.appendChild(remove);
                }
                chipsWrap.appendChild(chip);
            });
            renderIcons();
        }

        function renderOptions(filterText) {
            var q = (filterText || "").trim().toLowerCase();
            var filtered = q ? options.filter(function (o) { return o.label.toLowerCase().indexOf(q) !== -1; }) : options;
            optionsList.innerHTML = "";
            if (!filtered.length) {
                var empty = document.createElement("li");
                empty.className = "vw-msel-empty";
                empty.textContent = "No matches found";
                optionsList.appendChild(empty);
                return;
            }
            filtered.forEach(function (o) {
                var isChecked = selected.indexOf(o.value) !== -1;
                var li = document.createElement("li");
                li.className = "vw-msel-option" + (isChecked ? " is-checked" : "") + (o.disabled ? " is-disabled" : "");
                li.setAttribute("role", "option");
                li.setAttribute("aria-selected", isChecked ? "true" : "false");
                li.innerHTML = '<span class="vw-msel-option-check"><i data-lucide="check"></i></span><span>' + o.label + "</span>";
                if (!o.disabled) {
                    li.addEventListener("click", function () {
                        if (isChecked) selected = selected.filter(function (v) { return v !== o.value; });
                        else selected.push(o.value);
                        renderChips(); renderOptions(search.value); syncTarget();
                    });
                }
                optionsList.appendChild(li);
            });
            renderIcons();
        }

        function open() {
            if (isDisabled) return;
            document.querySelectorAll(".vw-msel.is-open").forEach(function (m) {
                if (m !== root) { m.classList.remove("is-open"); var p = m.querySelector(".vw-msel-panel"); if (p) p.hidden = true; }
            });
            root.classList.add("is-open");
            panel.hidden = false;
            control.setAttribute("aria-expanded", "true");
            search.value = "";
            renderOptions("");
            setTimeout(function () { search.focus(); }, 0);
        }
        function close() {
            root.classList.remove("is-open");
            panel.hidden = true;
            control.setAttribute("aria-expanded", "false");
        }

        control.addEventListener("click", function () {
            if (root.classList.contains("is-open")) close(); else open();
        });
        search.addEventListener("input", function () { renderOptions(search.value); });
        panel.addEventListener("keydown", function (e) {
            if (e.key === "Escape") { e.preventDefault(); close(); control.focus(); }
        });
        document.addEventListener("click", function (e) {
            if (!root.contains(e.target)) close();
        });

        if (isDisabled) {
            control.classList.add("is-disabled");
            control.disabled = true;
            control.setAttribute("aria-disabled", "true");
        }

        renderChips();
        syncTarget();
    }

    function initMultiSelectChips() {
        document.querySelectorAll("[data-msel]").forEach(enhanceMultiSelect);
    }
    window.vwInitMultiSelectChips = initMultiSelectChips;

    /* ---------------------------------------------------------------- */
    /* Sortable table headers — opt-in, client-side only.                 */
    /* Add data-sortable to a <table> and data-sort-key="anything" to     */
    /* any <th> in its <thead> to make that column clickable. Clicking    */
    /* re-orders the existing <tbody> rows already rendered by the         */
    /* server; it never re-fetches or changes what the server sends, so   */
    /* the page's default order on load is completely unaffected.         */
    /* Optional per-cell: data-sort-type="number" | "date" on the <th>    */
    /* (defaults to plain text comparison), and data-sort-value="..." on  */
    /* a <td> when the value to sort by isn't the same as its visible     */
    /* text (e.g. a formatted date or a name split across two lines).     */
    /* ---------------------------------------------------------------- */
    function initSortableTables() {
        document.querySelectorAll("table[data-sortable]").forEach(function (table) {
            var headerRow = table.tHead && table.tHead.rows[0];
            if (!headerRow) return;
            var headers = Array.prototype.filter.call(headerRow.cells, function (th) {
                return th.hasAttribute("data-sort-key");
            });
            if (!headers.length) return;

            headers.forEach(function (th) {
                th.classList.add("vw-sortable-th");
                th.setAttribute("role", "button");
                th.setAttribute("tabindex", "0");
                if (!th.getAttribute("aria-label")) {
                    th.setAttribute("aria-label", "Sort by " + th.textContent.trim());
                }
                if (!th.querySelector(".vw-sort-icon")) {
                    var icon = document.createElement("span");
                    icon.className = "vw-sort-icon";
                    icon.setAttribute("aria-hidden", "true");
                    icon.innerHTML = '<i data-lucide="chevrons-up-down"></i>';
                    th.appendChild(icon);
                }
                var activate = function () { sortTableByHeader(table, th, headers); };
                th.addEventListener("click", activate);
                th.addEventListener("keydown", function (e) {
                    if (e.key === "Enter" || e.key === " ") { e.preventDefault(); activate(); }
                });
            });
        });
        renderIcons();
    }

    function sortTableByHeader(table, activeHeader, allHeaders) {
        var tbody = table.tBodies[0];
        if (!tbody) return;
        var colIndex = Array.prototype.indexOf.call(activeHeader.parentNode.children, activeHeader);
        var type = activeHeader.getAttribute("data-sort-type") || "text";
        var nextDir = activeHeader.getAttribute("data-sort-dir") === "asc" ? "desc" : "asc";

        allHeaders.forEach(function (h) {
            h.removeAttribute("data-sort-dir");
            h.classList.remove("is-sorted");
            var icon = h.querySelector(".vw-sort-icon i");
            if (icon) icon.setAttribute("data-lucide", "chevrons-up-down");
        });
        activeHeader.setAttribute("data-sort-dir", nextDir);
        activeHeader.classList.add("is-sorted");
        var activeIcon = activeHeader.querySelector(".vw-sort-icon i");
        if (activeIcon) activeIcon.setAttribute("data-lucide", nextDir === "asc" ? "chevron-up" : "chevron-down");

        var rows = Array.prototype.slice.call(tbody.rows).filter(function (row) {
            return row.cells.length > colIndex && !row.hasAttribute("data-sort-exclude");
        });

        function valueOf(row) {
            var cell = row.cells[colIndex];
            var raw = cell.hasAttribute("data-sort-value") ? cell.getAttribute("data-sort-value") : cell.textContent;
            raw = raw.trim();
            if (type === "number") return parseFloat(raw.replace(/[^0-9.\-]/g, "")) || 0;
            if (type === "date") return new Date(raw).getTime() || 0;
            return raw.toLowerCase();
        }

        rows.sort(function (a, b) {
            var va = valueOf(a), vb = valueOf(b);
            var result = va < vb ? -1 : va > vb ? 1 : 0;
            return nextDir === "asc" ? result : -result;
        });

        rows.forEach(function (row) { tbody.appendChild(row); });
        renderIcons();
    }
    window.vwInitSortableTables = initSortableTables;

    /* ---------------------------------------------------------------- */
    /* Search boxes (.vw-search-box) — clear button + a11y wiring         */
    /* -----------------------------------------------------------------
       Purely a UX layer on top of whatever <form method="get"> the box
       already lives in: the clear button empties the input and, if the
       box sits inside a form, submits it so the page reflects "no
       filter" immediately — exactly what removing the query param by
       hand and pressing Enter would do. Nothing about the input's name,
       value binding, or the form's action/method is touched, so the
       existing server-side search/filter logic keeps working as-is. */
    /* ---------------------------------------------------------------- */
    function initSearchBoxes() {
        document.querySelectorAll(".vw-search-box").forEach(function (box) {
            if (box.dataset.wiredSearch === "1") return;
            box.dataset.wiredSearch = "1";

            var input = box.querySelector("input");
            if (!input) return;

            var clearBtn = box.querySelector(".vw-search-box-clear");
            if (!clearBtn) {
                clearBtn = document.createElement("button");
                clearBtn.type = "button";
                clearBtn.className = "vw-search-box-clear";
                clearBtn.setAttribute("aria-label", "Clear search");
                clearBtn.innerHTML = '<i data-lucide="x"></i>';
                box.appendChild(clearBtn);
                renderIcons();
            }

            function sync() {
                box.classList.toggle("has-value", input.value.trim().length > 0);
            }
            sync();

            input.addEventListener("input", sync);
            clearBtn.addEventListener("click", function () {
                input.value = "";
                sync();
                input.focus();
                var form = input.closest("form");
                if (form) form.requestSubmit ? form.requestSubmit() : form.submit();
            });
            // A quick keyboard escape hatch: Escape clears the field
            // without also closing menus/modals elsewhere on the page.
            input.addEventListener("keydown", function (e) {
                if (e.key === "Escape" && input.value) {
                    e.stopPropagation();
                    clearBtn.click();
                }
            });
        });
    }
    window.vwInitSearchBoxes = initSearchBoxes;

    /* ---------------------------------------------------------------- */
    /* Smart tables/lists — client-side search + pagination layer.        */
    /* Opt in with data-smart-table + id="..." on either:                 */
    /*   - a <table> whose <tbody> rows are already server-rendered, or   */
    /*   - any other container (e.g. a <div> of .ui-card "rows" like     */
    /*     Admin/Students.cshtml's candidate list) whose direct children  */
    /*     are already server-rendered.                                   */
    /* No server-side paging in either case. Sits alongside data-sortable */
    /* (initSortableTables above) for tables — sorting still reorders the */
    /* real rows, and a MutationObserver here just re-applies the current */
    /* search/page slice afterwards. Wire up the matching pieces with:    */
    /*   - Views/Shared/_TableToolbar.cshtml (search input + page-size)   */
    /*   - <div data-table-pager="tableId"></div> below the table/list    */
    /* Optional: put data-search-text="..." on a row/card to search       */
    /* against text that isn't in a visible cell (e.g. free-text notes).  */
    /* ---------------------------------------------------------------- */
    function initSmartTables() {
        document.querySelectorAll("[data-smart-table]").forEach(function (container) {
            var id = container.id;
            if (!id) return; // pager/toolbar wiring depends on a stable id
            var isTable = container.tagName === "TABLE";
            var rowsContainer = isTable ? container.tBodies[0] : container;
            if (!rowsContainer) return;

            var searchInput = document.querySelector('[data-smart-search="' + id + '"]');
            var pageSizeSelect = document.querySelector('[data-smart-pagesize="' + id + '"]');
            var pagerEl = document.querySelector('[data-table-pager="' + id + '"]');
            var countEl = document.querySelector('[data-smart-count="' + id + '"]');

            var defaultPageSize = parseInt(container.getAttribute("data-page-size"), 10) || 10;
            var state = { page: 1, pageSize: defaultPageSize, query: "" };

            if (pageSizeSelect && pageSizeSelect.value) {
                state.pageSize = pageSizeSelect.value === "all" ? Infinity : (parseInt(pageSizeSelect.value, 10) || defaultPageSize);
            }

            function dataRows() {
                return Array.prototype.slice.call(rowsContainer.children).filter(function (row) {
                    return !row.hasAttribute("data-sort-exclude");
                });
            }

            function rowMatches(row, q) {
                if (!q) return true;
                var haystack = row.hasAttribute("data-search-text")
                    ? row.getAttribute("data-search-text")
                    : row.textContent;
                return haystack.toLowerCase().indexOf(q) !== -1;
            }

            function ensureNoResultsRow(colCount) {
                var row = rowsContainer.querySelector('[data-smart-no-results="' + id + '"]');
                if (!row) {
                    if (isTable) {
                        row = document.createElement("tr");
                        row.setAttribute("data-smart-no-results", id);
                        row.setAttribute("data-sort-exclude", "");
                        var td = document.createElement("td");
                        td.colSpan = colCount || 1;
                        td.className = "px-5 py-8 text-center text-sm";
                        td.style.color = "var(--vw-text-muted)";
                        td.textContent = "No matching rows found.";
                        row.appendChild(td);
                    } else {
                        row = document.createElement("div");
                        row.setAttribute("data-smart-no-results", id);
                        row.setAttribute("data-sort-exclude", "");
                        row.className = "px-5 py-8 text-center text-sm";
                        row.style.color = "var(--vw-text-muted)";
                        row.textContent = "No matching results found.";
                    }
                    rowsContainer.appendChild(row);
                }
                return row;
            }

            function renderPager(totalPages) {
                if (!pagerEl) return;
                pagerEl.innerHTML = "";
                if (totalPages <= 1) return;

                var nav = document.createElement("nav");
                nav.className = "vw-pagination";
                nav.setAttribute("aria-label", "Pagination");

                var prevBtn = document.createElement("button");
                prevBtn.type = "button";
                prevBtn.className = "vw-page-btn vw-page-prev" + (state.page <= 1 ? " disabled" : "");
                prevBtn.disabled = state.page <= 1;
                prevBtn.setAttribute("aria-label", "Previous page");
                prevBtn.innerHTML = '<i data-lucide="chevron-left" class="h-4 w-4" aria-hidden="true"></i><span class="vw-page-btn-label">Previous</span>';
                prevBtn.addEventListener("click", function () { state.page -= 1; render(); });
                nav.appendChild(prevBtn);

                var numbers = document.createElement("div");
                numbers.className = "vw-page-numbers";
                var pagesToShow = [];
                for (var p = 1; p <= totalPages; p++) {
                    if (p === 1 || p === totalPages || Math.abs(p - state.page) <= 1) {
                        pagesToShow.push(p);
                    } else if (pagesToShow.length === 0 || pagesToShow[pagesToShow.length - 1] !== null) {
                        pagesToShow.push(null);
                    }
                }
                pagesToShow.forEach(function (p) {
                    if (p === null) {
                        var ellipsis = document.createElement("span");
                        ellipsis.className = "vw-page-ellipsis";
                        ellipsis.setAttribute("aria-hidden", "true");
                        ellipsis.textContent = "\u2026";
                        numbers.appendChild(ellipsis);
                        return;
                    }
                    var btn = document.createElement("button");
                    btn.type = "button";
                    btn.className = "vw-page-num" + (p === state.page ? " is-active" : "");
                    btn.textContent = String(p);
                    if (p === state.page) btn.setAttribute("aria-current", "page");
                    btn.addEventListener("click", function () { state.page = p; render(); });
                    numbers.appendChild(btn);
                });
                nav.appendChild(numbers);

                var mobileStatus = document.createElement("span");
                mobileStatus.className = "vw-page-mobile-status";
                mobileStatus.textContent = "Page " + state.page + " of " + totalPages;
                nav.appendChild(mobileStatus);

                var nextBtn = document.createElement("button");
                nextBtn.type = "button";
                nextBtn.className = "vw-page-btn vw-page-next" + (state.page >= totalPages ? " disabled" : "");
                nextBtn.disabled = state.page >= totalPages;
                nextBtn.setAttribute("aria-label", "Next page");
                nextBtn.innerHTML = '<span class="vw-page-btn-label">Next</span><i data-lucide="chevron-right" class="h-4 w-4" aria-hidden="true"></i>';
                nextBtn.addEventListener("click", function () { state.page += 1; render(); });
                nav.appendChild(nextBtn);

                pagerEl.appendChild(nav);
                renderIcons();
            }

            function render() {
                var rows = dataRows();
                var q = state.query.trim().toLowerCase();
                var filtered = rows.filter(function (row) { return rowMatches(row, q); });

                var pageSize = state.pageSize;
                var totalPages = pageSize === Infinity ? 1 : Math.max(1, Math.ceil(filtered.length / pageSize));
                if (state.page > totalPages) state.page = totalPages;
                if (state.page < 1) state.page = 1;
                var start = pageSize === Infinity ? 0 : (state.page - 1) * pageSize;
                var end = pageSize === Infinity ? filtered.length : start + pageSize;
                var visible = filtered.slice(start, end);

                rows.forEach(function (row) { row.style.display = "none"; });
                visible.forEach(function (row) { row.style.display = ""; });

                var headerCellCount = isTable && container.tHead && container.tHead.rows[0] ? container.tHead.rows[0].cells.length : 1;
                var noResultsRow = rowsContainer.querySelector('[data-smart-no-results="' + id + '"]');
                if (rows.length > 0 && filtered.length === 0) {
                    noResultsRow = ensureNoResultsRow(headerCellCount);
                    noResultsRow.style.display = "";
                } else if (noResultsRow) {
                    noResultsRow.style.display = "none";
                }

                if (countEl) {
                    if (rows.length === 0) {
                        countEl.textContent = "";
                    } else if (q) {
                        countEl.textContent = filtered.length + " of " + rows.length + (rows.length === 1 ? " row" : " rows");
                    } else {
                        countEl.textContent = rows.length + (rows.length === 1 ? " row" : " rows");
                    }
                }

                renderPager(totalPages);
            }

            if (searchInput) {
                searchInput.addEventListener("input", function () {
                    state.query = searchInput.value;
                    state.page = 1;
                    render();
                });
            }
            if (pageSizeSelect) {
                pageSizeSelect.addEventListener("change", function () {
                    state.pageSize = pageSizeSelect.value === "all" ? Infinity : (parseInt(pageSizeSelect.value, 10) || defaultPageSize);
                    state.page = 1;
                    render();
                });
            }

            // Sorting (initSortableTables) reorders the real <tbody> rows in
            // place; re-apply the current search/page slice afterwards
            // instead of duplicating sort logic here.
            if (window.MutationObserver) {
                var observer = new MutationObserver(function (mutations) {
                    var reordered = mutations.some(function (m) { return m.type === "childList"; });
                    if (reordered) render();
                });
                observer.observe(rowsContainer, { childList: true });
            }

            render();
        });
    }
    window.vwInitSmartTables = initSmartTables;

    /* ---------------------------------------------------------------- */
    /* Marks/CGPA scale sync (UX only — server enforces the real limit)  */
    /* ---------------------------------------------------------------- */
    function initMarksTypeSync() {
        var selects = document.querySelectorAll(".js-marks-type");
        if (!selects.length) return;
        var apply = function (select) {
            var container = select.closest(".edu-detail-fields");
            if (!container) return;
            var input = container.querySelector(".js-marks-value");
            var hint = container.querySelector(".js-marks-hint");
            if (!input) return;
            if (select.value === "CGPA") {
                input.max = "10";
                if (hint) hint.textContent = "e.g. 8.5 (CGPA is out of 10)";
            } else {
                input.max = "100";
                if (hint) hint.textContent = "e.g. 85 (Percentage is out of 100)";
            }
        };
        selects.forEach(function (select) {
            apply(select);
            select.addEventListener("change", function () { apply(select); });
        });
    }

    /* ---------------------------------------------------------------- */
    /* Future-only datetime pickers (UX only — server re-validates)      */
    /* ---------------------------------------------------------------- */
    function initFutureDatetimeInputs() {
        var inputs = document.querySelectorAll(".js-future-datetime");
        if (!inputs.length) return;
        // datetime-local's min/value need "YYYY-MM-DDTHH:mm" in *local* time —
        // toISOString() is UTC, so build it from the local field getters instead.
        var now = new Date();
        var pad = function (n) { return String(n).padStart(2, "0"); };
        var nowLocal = now.getFullYear() + "-" + pad(now.getMonth() + 1) + "-" + pad(now.getDate()) +
            "T" + pad(now.getHours()) + ":" + pad(now.getMinutes());
        inputs.forEach(function (input) {
            input.min = nowLocal;
        });
    }

    /* ---------------------------------------------------------------- */
    /* Boot                                                               */
    /* ---------------------------------------------------------------- */
    document.addEventListener("DOMContentLoaded", function () {
        initTheme();
        initHeroNav();
        initSidebar();
        syncSidebarFragmentActive();
        initMenus();
        initModals();
        initAccordions();
        initConfirmDialogs();
        initAlertDialogs();
        initInlineAlerts();
        initTabs();
        initStatCounters();
        initPasswordEnhancements();
        initLiveFieldValidation();
        initSearchableSelects();
        initMultiSelectChips();
        initSortableTables();
        initSearchBoxes();
        initSmartTables();
        initSkeletons();
        initMarksTypeSync();
        initFutureDatetimeInputs();
        renderIcons();

        document.querySelectorAll("[data-flash-message]").forEach(function (el) {
            createToast(el.getAttribute("data-flash-message"), el.getAttribute("data-flash-variant"));
        });
    });
})();
