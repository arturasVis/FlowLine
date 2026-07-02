// Thin Chart.js wrapper for the stats pages. Blazor re-renders call these repeatedly for
// the same canvas (date-range changes), so every renderer destroys the previous Chart
// instance first — Chart.js throws if a canvas is reused without that.
(function () {
    const charts = {};

    function fmtDuration(totalSeconds) {
        const s = Math.round(totalSeconds);
        const h = Math.floor(s / 3600);
        const m = Math.floor((s % 3600) / 60);
        const sec = s % 60;
        if (h > 0) return `${h}h ${m}m`;
        if (m > 0) return `${m}m ${sec}s`;
        return `${sec}s`;
    }

    // Axis ticks land on fractional seconds when all values are tiny; show tenths there so
    // adjacent ticks don't all collapse to the same "0s" label.
    function fmtTick(totalSeconds) {
        return totalSeconds < 10 ? `${Math.round(totalSeconds * 10) / 10}s` : fmtDuration(totalSeconds);
    }

    function destroy(canvasId) {
        if (charts[canvasId]) {
            charts[canvasId].destroy();
            delete charts[canvasId];
        }
    }

    function make(canvasId, config) {
        destroy(canvasId);
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;
        charts[canvasId] = new Chart(canvas, config);
    }

    const palette = ['#4f6df5', '#22b07d', '#f5a623', '#e05563', '#8e6df5', '#2fa8c9'];

    const baseOptions = {
        responsive: true,
        maintainAspectRatio: false,
        animation: { duration: 300 },
        plugins: { legend: { labels: { boxWidth: 12 } } },
    };

    window.flowlineCharts = {
        destroy,

        // Horizontal bars of durations (values in seconds) — per-step / per-SKU averages.
        durationBar(canvasId, labels, values, seriesLabel) {
            make(canvasId, {
                type: 'bar',
                data: {
                    labels,
                    datasets: [{ label: seriesLabel, data: values, backgroundColor: palette[0] }],
                },
                options: {
                    ...baseOptions,
                    indexAxis: 'y',
                    plugins: {
                        legend: { display: false },
                        tooltip: { callbacks: { label: c => `${c.dataset.label}: ${fmtDuration(c.parsed.x)}` } },
                    },
                    scales: { x: { beginAtZero: true, ticks: { callback: v => fmtTick(v) } } },
                },
            });
        },

        // Side-by-side duration bars (seconds) — staff vs. workflow average per step.
        durationGroupedBar(canvasId, labels, seriesLabels, seriesValues) {
            make(canvasId, {
                type: 'bar',
                data: {
                    labels,
                    datasets: seriesLabels.map((label, i) => ({
                        label,
                        data: seriesValues[i],
                        backgroundColor: palette[i % palette.length],
                    })),
                },
                options: {
                    ...baseOptions,
                    indexAxis: 'y',
                    plugins: {
                        ...baseOptions.plugins,
                        tooltip: { callbacks: { label: c => `${c.dataset.label}: ${fmtDuration(c.parsed.x)}` } },
                    },
                    scales: { x: { beginAtZero: true, ticks: { callback: v => fmtTick(v) } } },
                },
            });
        },

        // Units-per-day trend line.
        countLine(canvasId, labels, values, seriesLabel) {
            make(canvasId, {
                type: 'line',
                data: {
                    labels,
                    datasets: [{
                        label: seriesLabel,
                        data: values,
                        borderColor: palette[1],
                        backgroundColor: palette[1] + '33',
                        fill: true,
                        tension: 0.25,
                        pointRadius: 3,
                    }],
                },
                options: {
                    ...baseOptions,
                    plugins: { legend: { display: false } },
                    scales: { y: { beginAtZero: true, ticks: { precision: 0 } } },
                },
            });
        },

        // Vertical count bars — units built per SKU.
        countBar(canvasId, labels, values, seriesLabel) {
            make(canvasId, {
                type: 'bar',
                data: {
                    labels,
                    datasets: [{ label: seriesLabel, data: values, backgroundColor: palette[2] }],
                },
                options: {
                    ...baseOptions,
                    plugins: { legend: { display: false } },
                    scales: { y: { beginAtZero: true, ticks: { precision: 0 } } },
                },
            });
        },
    };
})();
