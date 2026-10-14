// Dashboard ageing chart (Chart.js). Data is embedded as JSON by the server (non-executable script block).
(function () {
    "use strict";

    var canvas = document.getElementById("agingChart");
    var source = document.getElementById("agingData");
    if (!canvas || !source || !window.Chart) {
        return;
    }

    var data = JSON.parse(source.textContent);
    var money = new Intl.NumberFormat("en-US", { maximumFractionDigits: 0 });

    new window.Chart(canvas, {
        type: "bar",
        data: {
            labels: data.labels,
            datasets: [{
                label: "Pending applications",
                data: data.counts,
                backgroundColor: ["#2f66ab", "#c9a227", "#b02a37"],
                borderRadius: 8,
                maxBarThickness: 64
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: { display: false },
                tooltip: {
                    callbacks: {
                        afterLabel: function (context) {
                            return "LKR " + money.format(data.amounts[context.dataIndex]);
                        }
                    }
                }
            },
            scales: {
                y: { beginAtZero: true, ticks: { precision: 0 }, grid: { color: "#eef3fa" } },
                x: { grid: { display: false } }
            }
        }
    });
})();
