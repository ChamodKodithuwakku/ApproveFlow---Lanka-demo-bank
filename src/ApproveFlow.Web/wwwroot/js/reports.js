// Reports page charts (Chart.js). Data is embedded by the server as a non-executable JSON block.
(function () {
    "use strict";

    var source = document.getElementById("reportData");
    if (!source || !window.Chart) {
        return;
    }

    var data = JSON.parse(source.textContent);
    var statusColours = {
        "Draft": "#8592a8", "Submitted": "#2f66ab", "Pending 2nd approval": "#5b3cc4",
        "Returned": "#c98a00", "Approved": "#198754", "Rejected": "#b02a37"
    };

    var statusCanvas = document.getElementById("statusChart");
    if (statusCanvas && data.statusLabels) {
        new window.Chart(statusCanvas, {
            type: "doughnut",
            data: {
                labels: data.statusLabels,
                datasets: [{
                    data: data.statusCounts,
                    backgroundColor: data.statusLabels.map(function (l) { return statusColours[l] || "#1d4e89"; }),
                    borderWidth: 2
                }]
            },
            options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { position: "right" } }, cutout: "62%" }
        });
    }

    var branchCanvas = document.getElementById("branchChart");
    if (branchCanvas && data.branchLabels) {
        new window.Chart(branchCanvas, {
            type: "bar",
            data: {
                labels: data.branchLabels,
                datasets: [
                    { label: "Approved", data: data.branchApproved, backgroundColor: "#198754", borderRadius: 6 },
                    { label: "Rejected", data: data.branchRejected, backgroundColor: "#b02a37", borderRadius: 6 },
                    { label: "Pending", data: data.branchPending, backgroundColor: "#2f66ab", borderRadius: 6 }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                scales: { y: { beginAtZero: true, ticks: { precision: 0 }, grid: { color: "#eef3fa" } }, x: { grid: { display: false } } }
            }
        });
    }
})();
