// Chart.js 4 rendering for TimeSeriesChart.razor. Chart.js, Luxon and the Luxon adapter are loaded
// as globals in _Layout.cshtml; this module only turns the payload from Blazor into a chart.

const ZONE = 'Europe/Warsaw';
const LOCALE = 'pl';

const charts = new Map(); // canvas -> { chart, payload }

const css = name => getComputedStyle(document.documentElement).getPropertyValue(name).trim();

const numberFormats = new Map();
function formatNumber(value, decimals) {
    if (!numberFormats.has(decimals)) {
        numberFormats.set(decimals, new Intl.NumberFormat('pl-PL', { minimumFractionDigits: decimals, maximumFractionDigits: decimals }));
    }
    return numberFormats.get(decimals).format(value);
}

function withAlpha(color, alpha) {
    const hex = color.replace('#', '');
    const [r, g, b] = [0, 2, 4].map(i => parseInt(hex.substring(i, i + 2), 16));
    return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}

function tooltipTitle(ms, bucketMinutes) {
    const start = window.luxon.DateTime.fromMillis(ms, { zone: ZONE }).setLocale(LOCALE);
    if (bucketMinutes === 0) return start.toFormat('ccc d LLL, HH:mm');
    if (bucketMinutes < 1440) return `${start.toFormat('ccc d LLL, HH:mm')}–${start.plus({ minutes: bucketMinutes }).toFormat('HH:mm')}`;
    if (bucketMinutes === 1440) return start.toFormat('cccc d LLL yyyy');
    return `tydzień od ${start.toFormat('d LLL yyyy')}`;
}

// Vertical hairline at the hovered x, so readers aim at a time rather than at a 2px line.
const crosshair = {
    id: 'smokCrosshair',
    afterDatasetsDraw(chart) {
        const active = chart.tooltip?.getActiveElements?.() ?? [];
        if (!active.length) return;
        const { ctx, chartArea } = chart;
        const x = active[0].element.x;
        ctx.save();
        ctx.strokeStyle = css('--chart-axis');
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.moveTo(x, chartArea.top);
        ctx.lineTo(x, chartArea.bottom);
        ctx.stroke();
        ctx.restore();
    }
};

// Dashed reference lines (air-quality guidelines) with a label at the right edge.
const referenceLines = {
    id: 'smokReferenceLines',
    beforeDatasetsDraw(chart, _args, options) {
        const lines = options.lines ?? [];
        if (!lines.length) return;
        const { ctx, chartArea, scales: { y } } = chart;
        ctx.save();
        ctx.font = `12px ${css('--font-sans')}`;
        ctx.textAlign = 'right';
        let previousY = -Infinity;
        for (const line of [...lines].sort((a, b) => b.value - a.value)) {
            if (line.value < y.min || line.value > y.max) continue;
            const yPos = Math.round(y.getPixelForValue(line.value)) + 0.5;
            // When two lines sit close together, put the lower one's label underneath it.
            const below = yPos - previousY < 16;
            previousY = yPos;
            ctx.textBaseline = below ? 'top' : 'bottom';
            ctx.strokeStyle = css('--chart-reference');
            ctx.setLineDash([4, 4]);
            ctx.lineWidth = 1;
            ctx.beginPath();
            ctx.moveTo(chartArea.left, yPos);
            ctx.lineTo(chartArea.right, yPos);
            ctx.stroke();
            ctx.fillStyle = css('--text-secondary');
            ctx.fillText(line.label, chartArea.right - 4, below ? yPos + 3 : yPos - 3);
        }
        ctx.restore();
    }
};

function buildConfig(payload) {
    const datasets = [];
    for (const series of payload.series) {
        const color = css(series.color);
        if (payload.band) {
            datasets.push({ role: 'min', label: `${series.label} min`, data: series.min, borderWidth: 0, pointRadius: 0, pointHoverRadius: 0, fill: false });
            datasets.push({ role: 'max', label: `${series.label} max`, data: series.max, borderWidth: 0, pointRadius: 0, pointHoverRadius: 0, fill: '-1', backgroundColor: withAlpha(color, 0.16) });
        }
        datasets.push({
            role: 'avg',
            label: series.label,
            data: series.avg,
            rangeMin: series.min,
            rangeMax: series.max,
            borderColor: color,
            backgroundColor: color,
            borderWidth: 2,
            pointRadius: 0,
            pointHoverRadius: 4,
            pointHoverBorderWidth: 2,
            pointHoverBorderColor: css('--surface-1'),
            tension: 0.2,
            fill: false
        });
    }

    const refMax = Math.max(0, ...payload.references.map(r => r.value));
    const narrow = window.matchMedia('(max-width: 600px)').matches;

    return {
        type: 'line',
        data: { labels: payload.t, datasets },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            animation: false,
            spanGaps: false,
            normalized: true,
            interaction: { mode: 'index', intersect: false },
            layout: { padding: { top: 4 } },
            scales: {
                x: {
                    type: 'time',
                    adapters: { date: { zone: ZONE, locale: LOCALE } },
                    time: { displayFormats: { minute: 'HH:mm', hour: 'HH:mm', day: 'd LLL', week: 'd LLL', month: 'LLL yyyy', quarter: 'LLL yyyy', year: 'yyyy' } },
                    min: payload.from,
                    max: payload.to,
                    grid: { display: false },
                    border: { color: css('--chart-axis') },
                    ticks: { color: css('--text-muted'), maxRotation: 0, autoSkipPadding: 16, maxTicksLimit: narrow ? 4 : 8, font: { size: 12 } }
                },
                y: {
                    beginAtZero: payload.zeroBased,
                    suggestedMax: refMax > 0 ? refMax * 1.15 : undefined,
                    grace: '5%',
                    grid: { color: css('--chart-grid') },
                    border: { display: false },
                    ticks: { color: css('--text-muted'), maxTicksLimit: narrow ? 5 : 6, font: { size: 12 }, callback: v => formatNumber(v, Number.isInteger(v) ? 0 : payload.decimals) }
                }
            },
            plugins: {
                legend: {
                    display: payload.series.length > 1,
                    align: 'start',
                    labels: {
                        color: css('--text-secondary'),
                        usePointStyle: true,
                        pointStyle: 'line',
                        boxWidth: 16,
                        padding: 14,
                        font: { size: 13 },
                        filter: item => datasets[item.datasetIndex].role === 'avg'
                    },
                    onClick: () => { } // keep every series visible; hiding one would change what the axis means
                },
                tooltip: {
                    backgroundColor: css('--tooltip-bg'),
                    titleColor: css('--text-secondary'),
                    bodyColor: css('--text-primary'),
                    borderColor: css('--border'),
                    borderWidth: 1,
                    padding: 10,
                    boxWidth: 12,
                    boxHeight: 2,
                    usePointStyle: false,
                    titleFont: { weight: 'normal', size: 12 },
                    bodyFont: { size: 13 },
                    filter: item => item.dataset.role === 'avg' && item.raw !== null,
                    callbacks: {
                        title: items => items.length ? tooltipTitle(items[0].parsed.x, payload.bucketMinutes) : '',
                        label: item => {
                            const value = `${formatNumber(item.raw, payload.decimals)} ${payload.unit}`;
                            const ds = item.dataset;
                            const range = payload.bucketMinutes > 0
                                ? `  (${formatNumber(ds.rangeMin[item.dataIndex], payload.decimals)}–${formatNumber(ds.rangeMax[item.dataIndex], payload.decimals)})`
                                : '';
                            return payload.series.length > 1 ? `${value}  ${ds.label}${range}` : `${value}${range}`;
                        },
                        labelColor: item => ({ borderColor: item.dataset.borderColor, backgroundColor: item.dataset.borderColor })
                    }
                },
                smokReferenceLines: { lines: payload.references }
            }
        },
        plugins: [crosshair, referenceLines]
    };
}

export function render(canvas, payload) {
    if (!window.Chart || !window.luxon) {
        throw new Error('Chart libraries did not load');
    }
    const existing = charts.get(canvas);
    existing?.chart.destroy();
    const chart = new window.Chart(canvas, buildConfig(payload));
    charts.set(canvas, { chart, payload });
}

export function destroy(canvas) {
    charts.get(canvas)?.chart.destroy();
    charts.delete(canvas);
}

export function download(fileName, content, mimeType) {
    const url = URL.createObjectURL(new Blob(['﻿' + content], { type: mimeType }));
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
}

// Colours come from CSS custom properties, so redraw when the colour scheme flips.
window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
    for (const [canvas, { payload }] of charts) render(canvas, payload);
});
