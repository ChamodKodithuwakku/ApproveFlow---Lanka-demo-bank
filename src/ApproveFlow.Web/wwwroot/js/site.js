// ApproveFlow shared UI behaviour. Loaded on every page; no inline script is used anywhere (strict CSP).
(function () {
    "use strict";

    // Confirmation dialog for any <form data-confirm="..."> (submit, withdraw, unlock...).
    var modalElement = document.getElementById("afConfirmModal");
    var pendingForm = null;
    var modal = modalElement && window.bootstrap ? new window.bootstrap.Modal(modalElement) : null;

    document.addEventListener("submit", function (event) {
        var form = event.target;
        if (!(form instanceof HTMLFormElement) || !form.dataset.confirm || form.dataset.confirmed === "true") {
            return;
        }

        event.preventDefault();
        if (!modal) {
            if (window.confirm(form.dataset.confirm)) {
                form.dataset.confirmed = "true";
                form.submit();
            }
            return;
        }

        pendingForm = form;
        document.getElementById("afConfirmTitle").textContent = form.dataset.confirmTitle || "Please confirm";
        document.getElementById("afConfirmBody").textContent = form.dataset.confirm;
        var ok = document.getElementById("afConfirmOk");
        ok.textContent = form.dataset.confirmButton || "Confirm";
        ok.className = "btn btn-" + (form.dataset.confirmVariant || "primary");
        modal.show();
    });

    var okButton = document.getElementById("afConfirmOk");
    if (okButton) {
        okButton.addEventListener("click", function () {
            if (pendingForm) {
                pendingForm.dataset.confirmed = "true";
                okButton.disabled = true;
                pendingForm.submit();
            }
        });
    }

    if (modalElement) {
        modalElement.addEventListener("hidden.bs.modal", function () {
            pendingForm = null;
            if (okButton) {
                okButton.disabled = false;
            }
        });
    }

    // Prevent double submission of decision dialogs and other POST forms.
    document.addEventListener("submit", function (event) {
        var form = event.target;
        if (form instanceof HTMLFormElement && form.method.toLowerCase() === "post" && !event.defaultPrevented) {
            if (typeof form.checkValidity === "function" && !form.checkValidity()) {
                return;
            }
            window.setTimeout(function () {
                form.querySelectorAll("button[type=submit]").forEach(function (b) { b.disabled = true; });
            }, 0);
        }
    });

    // Filters that apply immediately (e.g. "Only my applications" switch).
    document.querySelectorAll("[data-autosubmit]").forEach(function (input) {
        input.addEventListener("change", function () {
            if (input.form) {
                input.form.submit();
            }
        });
    });
})();
