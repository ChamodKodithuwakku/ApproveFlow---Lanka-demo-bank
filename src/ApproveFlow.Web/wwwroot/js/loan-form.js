// Live EMI and debt-burden ratio for the loan application form.
// Mirrors ApproveFlow.Domain.Rules.LoanCalculator; the server recalculates authoritatively on save.
(function () {
    "use strict";

    var form = document.getElementById("loanForm");
    if (!form) {
        return;
    }

    var fields = {
        product: document.getElementById("LoanProductId"),
        amount: document.getElementById("Amount"),
        tenure: document.getElementById("TenureMonths"),
        rate: document.getElementById("InterestRate"),
        income: document.getElementById("MonthlyIncome"),
        obligations: document.getElementById("ExistingObligations")
    };
    var out = {
        emi: document.getElementById("calcEmi"),
        total: document.getElementById("calcTotal"),
        interest: document.getElementById("calcInterest"),
        dbr: document.getElementById("calcDbr"),
        bar: document.getElementById("calcDbrBar"),
        note: document.getElementById("calcDbrNote"),
        limits: document.getElementById("productLimits")
    };

    var money = new Intl.NumberFormat("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

    function num(input) {
        var value = parseFloat(input && input.value);
        return isFinite(value) ? value : NaN;
    }

    function monthlyInstallment(principal, annualRate, months) {
        if (!(principal > 0) || !(months > 0) || !(annualRate >= 0)) {
            return NaN;
        }
        if (annualRate === 0) {
            return principal / months;
        }
        var r = annualRate / 1200;
        var growth = Math.pow(1 + r, months);
        return principal * r * growth / (growth - 1);
    }

    function selectedProduct() {
        var option = fields.product && fields.product.selectedOptions[0];
        if (!option || !option.value) {
            return null;
        }
        return {
            minAmount: parseFloat(option.dataset.minAmount),
            maxAmount: parseFloat(option.dataset.maxAmount),
            minTenure: parseInt(option.dataset.minTenure, 10),
            maxTenure: parseInt(option.dataset.maxTenure, 10),
            rate: parseFloat(option.dataset.rate)
        };
    }

    function showLimits(product) {
        if (!out.limits) {
            return;
        }
        out.limits.textContent = product
            ? "Limits: LKR " + money.format(product.minAmount) + " – " + money.format(product.maxAmount) +
              ", " + product.minTenure + "–" + product.maxTenure + " months. Default rate " + product.rate.toFixed(2) + "%."
            : "";
    }

    function update() {
        var principal = num(fields.amount);
        var months = num(fields.tenure);
        var rate = num(fields.rate);
        var income = num(fields.income);
        var obligations = num(fields.obligations) || 0;

        var emi = monthlyInstallment(principal, rate, months);
        if (isNaN(emi)) {
            out.emi.textContent = out.total.textContent = out.interest.textContent = "—";
        } else {
            out.emi.textContent = "LKR " + money.format(emi);
            out.total.textContent = "LKR " + money.format(emi * months);
            out.interest.textContent = "LKR " + money.format(emi * months - principal);
        }

        out.dbr.classList.remove("af-dbr-low", "af-dbr-moderate", "af-dbr-high");
        if (isNaN(emi) || !(income > 0)) {
            out.dbr.textContent = "—";
            out.bar.style.width = "0";
            out.note.textContent = "Enter amount, tenure, rate and income to see the ratio.";
            return;
        }

        var dbr = (obligations + emi) / income * 100;
        out.dbr.textContent = dbr.toFixed(2) + "%";
        out.bar.style.width = Math.min(dbr, 100) + "%";
        if (dbr > 60) {
            out.dbr.classList.add("af-dbr-high");
            out.note.textContent = "High: above the 60% policy threshold. Expect additional scrutiny or rejection.";
        } else if (dbr > 40) {
            out.dbr.classList.add("af-dbr-moderate");
            out.note.textContent = "Moderate: within policy, but repayment capacity should be verified carefully.";
        } else {
            out.dbr.classList.add("af-dbr-low");
            out.note.textContent = "Low: comfortable repayment capacity.";
        }
    }

    if (fields.product) {
        fields.product.addEventListener("change", function () {
            var product = selectedProduct();
            showLimits(product);
            if (product && fields.rate && !fields.rate.value) {
                fields.rate.value = product.rate.toFixed(2);
            }
            update();
        });
    }

    ["amount", "tenure", "rate", "income", "obligations"].forEach(function (key) {
        if (fields[key]) {
            fields[key].addEventListener("input", update);
        }
    });

    showLimits(selectedProduct());
    update();
})();
