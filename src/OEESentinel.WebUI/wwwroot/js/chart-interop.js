const charts = {};

export function createGauge(canvasId, label, value, color) {
    const ctx = document.getElementById(canvasId);
    if (!ctx) return;

    charts[canvasId] = new Chart(ctx, {
        type: 'doughnut',
        data: {
            labels: [label, 'Loss'],
            datasets: [{
                data: [value, 100 - value],
                backgroundColor: [color, '#e9ecef'],
                borderWidth: 0,
                cutout: '75%'
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: { display: false },
                tooltip: { enabled: false }
            }
        }
    });
}

export function createBarChart(canvasId, labels, data) {
    const ctx = document.getElementById(canvasId);
    if (!ctx) return;

    charts[canvasId] = new Chart(ctx, {
        type: 'bar',
        data: {
            labels: labels,
            datasets: [{
                label: 'Downtime (seconds)',
                data: data,
                backgroundColor: '#dc3545', // Bootstrap danger red
                borderRadius: 4
            }]
        },
        options: {
            indexAxis: 'y', // Horizontal bars
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: { display: false }
            },
            scales: {
                x: { beginAtZero: true }
            }
        }
    });
}

export function updateChart(canvasId, value) {
    const chart = charts[canvasId];
    if (chart && chart.config.type === 'doughnut') {
        chart.data.datasets[0].data = [value, 100 - value];
        chart.update();
    }
}

export function destroyChart(canvasId) {
    if (charts[canvasId]) {
        charts[canvasId].destroy();
        delete charts[canvasId];
    }
}