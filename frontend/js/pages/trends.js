import { getTrends } from '../api.js';
import { h, clear, loading, toast, emptyState } from '../components.js';

let chartInstance = null;

export async function render(container, { signal }) {
  clear(container);

  loadChartLibrary();

  container.appendChild(
    h('div', { className: 'page-header' },
      h('h1', { className: 'page-title' }, 'Trend Analysis'),
      h('p', { className: 'page-subtitle' }, 'Research trend analysis across years'),
    )
  );

  const filterRow = h('div', { className: 'filter-row', style: 'margin-bottom: var(--s-6)' });

  const fieldInput = h('input', {
    className: 'input',
    type: 'text',
    id: 'trend-field',
    placeholder: 'Research field (e.g., machine learning)',
    style: 'flex: 1'
  });

  const startYearInput = h('input', {
    className: 'input',
    type: 'number',
    id: 'trend-start-year',
    placeholder: 'Start Year',
    style: 'width: 120px'
  });

  const endYearInput = h('input', {
    className: 'input',
    type: 'number',
    id: 'trend-end-year',
    placeholder: 'End Year',
    style: 'width: 120px'
  });

  const loadBtn = h('button', {
    className: 'btn btn-primary',
    onClick: () => loadTrends(),
  }, 'LOAD TRENDS');

  filterRow.appendChild(fieldInput);
  filterRow.appendChild(startYearInput);
  filterRow.appendChild(endYearInput);
  filterRow.appendChild(loadBtn);
  container.appendChild(filterRow);

  const chartContainer = h('div', { className: 'section' },
    h('div', { className: 'section-header' },
      h('h2', { className: 'section-title' }, 'Topic Trends Over Time')
    ),
    h('div', { style: 'position: relative; height: 400px;' },
      h('canvas', { id: 'trend-chart' })
    )
  );
  container.appendChild(chartContainer);

  const themesContainer = h('div', { className: 'section', id: 'themes-container' });
  container.appendChild(themesContainer);

  loadTrends();

  async function loadTrends() {
    const field = fieldInput.value.trim() || undefined;
    const startYear = parseInt(startYearInput.value) || undefined;
    const endYear = parseInt(endYearInput.value) || undefined;

    const loadingEl = h('div', { className: 'loading' },
      h('div', { className: 'spinner' }),
      h('span', { className: 'loading-text' }, 'Analyzing trends\u2026'),
    );
    chartContainer.appendChild(loadingEl);

    try {
      const data = await getTrends({ field, startYear, endYear }, signal);
      renderTrends(data);
    } catch (err) {
      if (err.name === 'AbortError') return;
      toast(err.message, 'error');
      renderError(err.message);
    }
  }

  function renderTrends(data) {
    const existingLoading = chartContainer.querySelector('.loading');
    if (existingLoading) existingLoading.remove();

    if (!data.buckets || data.buckets.length === 0) {
      chartContainer.appendChild(emptyState('No Data', 'No trend data available for the selected range.'));
      return;
    }

    renderChart(data);
    renderThemes(data);
  }

  function renderChart(data) {
    const canvas = document.getElementById('trend-chart');
    if (!canvas || typeof Chart === 'undefined') return;

    if (chartInstance) {
      chartInstance.destroy();
    }

    const allTopics = new Map();
    data.buckets.forEach(bucket => {
      bucket.topics.forEach(topic => {
        if (!allTopics.has(topic.topic)) {
          allTopics.set(topic.topic, []);
        }
      });
    });

    const labels = data.buckets.map(b => b.year.toString());

    const datasets = [];
    const topicColors = [
      '#4f46e5', '#06b6d4', '#10b981', '#f59e0b', '#ef4444',
      '#8b5cf6', '#ec4899', '#14b8a6', '#f97316', '#6366f1'
    ];

    let colorIndex = 0;
    for (const [topicName] of allTopics) {
      const color = topicColors[colorIndex % topicColors.length];
      const values = data.buckets.map(bucket => {
        const found = bucket.topics.find(t => t.topic === topicName);
        return found ? found.paperCount : 0;
      });

      datasets.push({
        label: topicName,
        data: values,
        borderColor: color,
        backgroundColor: color + '20',
        tension: 0.3,
        fill: false
      });
      colorIndex++;
    }

    chartInstance = new Chart(canvas, {
      type: 'line',
      data: { labels, datasets },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: {
            position: 'bottom',
            labels: { boxWidth: 12, padding: 8 }
          },
          tooltip: {
            callbacks: {
              label: ctx => `${ctx.dataset.label}: ${ctx.raw} papers`
            }
          }
        },
        scales: {
          x: { title: { display: true, text: 'Year' } },
          y: { title: { display: true, text: 'Paper Count' }, beginAtZero: true }
        }
      }
    });
  }

  function renderThemes(data) {
    clear(themesContainer);

    themesContainer.appendChild(
      h('div', { className: 'section-header' },
        h('h2', { className: 'section-title' }, 'Emerging & Declining Themes')
      )
    );

    const themesRow = h('div', { className: 'themes-grid', style: 'display: grid; grid-template-columns: 1fr 1fr; gap: var(--s-4); margin-top: var(--s-4);' });

    const emergingDiv = h('div', { className: 'theme-card', style: 'padding: var(--s-4); border: 1px solid var(--c-green); border-radius: var(--radius);' });
    emergingDiv.appendChild(h('h3', { style: 'color: var(--c-green); margin-bottom: var(--s-3);' }, '\u2191 Emerging Themes'));
    if (data.emergingThemes && data.emergingThemes.length > 0) {
      const list = h('ul', { style: 'list-style: none; padding: 0; margin: 0;' });
      data.emergingThemes.forEach(theme => {
        list.appendChild(h('li', { style: 'padding: 4px 0; border-bottom: 1px solid var(--c-border);' }, theme));
      });
      emergingDiv.appendChild(list);
    } else {
      emergingDiv.appendChild(h('p', { style: 'color: var(--c-text-secondary);' }, 'No emerging themes detected.'));
    }

    const decliningDiv = h('div', { className: 'theme-card', style: 'padding: var(--s-4); border: 1px solid var(--c-red); border-radius: var(--radius);' });
    decliningDiv.appendChild(h('h3', { style: 'color: var(--c-red); margin-bottom: var(--s-3);' }, '\u2193 Declining Themes'));
    if (data.decliningThemes && data.decliningThemes.length > 0) {
      const list = h('ul', { style: 'list-style: none; padding: 0; margin: 0;' });
      data.decliningThemes.forEach(theme => {
        list.appendChild(h('li', { style: 'padding: 4px 0; border-bottom: 1px solid var(--c-border);' }, theme));
      });
      decliningDiv.appendChild(list);
    } else {
      decliningDiv.appendChild(h('p', { style: 'color: var(--c-text-secondary);' }, 'No declining themes detected.'));
    }

    themesRow.appendChild(emergingDiv);
    themesRow.appendChild(decliningDiv);
    themesContainer.appendChild(themesRow);
  }

  function renderError(message) {
    const existingLoading = chartContainer.querySelector('.loading');
    if (existingLoading) existingLoading.remove();
    chartContainer.appendChild(emptyState('Error', message));
  }
}

function loadChartLibrary() {
  if (document.querySelector('script[src*="chart.js"]')) return;
  const script = document.createElement('script');
  script.src = 'https://cdn.jsdelivr.net/npm/chart.js@4.4.0/dist/chart.umd.min.js';
  document.head.appendChild(script);
}