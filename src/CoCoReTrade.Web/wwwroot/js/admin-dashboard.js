(() => {
  'use strict';
  const root = document.querySelector('[data-dashboard]');
  if (!root) return;
  const form = root.querySelector('[data-dashboard-filter]');
  const period = form.elements.namedItem('period');
  const from = form.elements.namedItem('from');
  const through = form.elements.namedItem('through');
  const feedback = root.querySelector('[data-dashboard-feedback]');
  const chartObjects = new Map();
  const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
  const number = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 0 });
  const percent = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 1 });
  const compact = value => Math.abs(value) >= 1e9 ? percent.format(value / 1e9) + ' tỷ' :
    Math.abs(value) >= 1e6 ? percent.format(value / 1e6) + ' triệu' :
    Math.abs(value) >= 1e3 ? percent.format(value / 1e3) + ' nghìn' : number.format(value);
  let request;
  let revision = 0;
  let latest = JSON.parse(root.querySelector('#dashboard-data').textContent);
  const theme = getComputedStyle(root);
  const color = name => theme.getPropertyValue('--chart-' + name).trim();
  const palette = { pink: color('pink'), pinkLine: color('pink-line'), blue: color('blue'),
    blueLine: color('blue-line'), green: color('green'), yellow: color('yellow'), rose: color('rose'), neutral: color('neutral') };
  const orderColors = [palette.yellow, palette.blue, palette.rose, palette.green, palette.neutral];
  const tooltipTheme = { backgroundColor: '#fff', titleColor: '#000', bodyColor: '#000', footerColor: '#000',
    borderColor: '#e5e7eb', borderWidth: 1, padding: 11, cornerRadius: 7 };
  const formatDate = value => value.slice(0, 10).split('-').reverse().join('/');
  function destination(key, datasetIndex, index, data) {
    const bucket = key === 'revenue' || key === 'sales' ? data.series[index] : data.range;
    if (!bucket) return null;
    const params = new URLSearchParams({ from: bucket.from, through: bucket.through });
    let path;
    let fragment = '';
    if (key === 'revenue' && data.canViewFinancials) {
      path = '/Admin/Reports/Revenue'; params.set('period', 'custom');
      fragment = datasetIndex === 1 ? '#report-commission' : '#report-revenue';
    } else if (key === 'sales' && data.canViewOrders) {
      path = '/Admin/Orders';
      if (datasetIndex === 1) { params.set('status', 'Completed'); params.set('paymentStatus', 'Paid'); params.set('basis', 'completed'); }
    } else if (key === 'orders' && data.canViewOrders && data.orderStatuses[index]) {
      path = '/Admin/Orders'; params.set('status', data.orderStatuses[index].key);
    } else if (key === 'products' && data.productStatuses[index]) {
      const status = data.productStatuses[index].key;
      if (status === 'Active' && root.querySelector('[data-metric="active"][href]')) {
        path = '/Admin/Products'; params.set('status', 'Active');
      } else if (status === 'PendingApproval') path = '/Admin/Inspection';
      else if (status === 'Sold' && data.canViewOrders) {
        path = '/Admin/Orders'; params.set('status', 'Completed'); params.set('paymentStatus', 'Paid'); params.set('basis', 'completed');
      }
    }
    return path ? path + '?' + params.toString() + fragment : null;
  }
  function drilldownOptions(key, data, options) {
    const selected = (event, chart) => chart.getElementsAtEventForMode(event, 'nearest', { intersect: true }, true)[0];
    options.onClick = (event, _, chart) => {
      const hit = selected(event, chart);
      const url = hit && destination(key, hit.datasetIndex, hit.index, data);
      if (url) window.location.assign(url);
    };
    options.onHover = (event, _, chart) => {
      const hit = selected(event, chart);
      chart.canvas.style.cursor = hit && destination(key, hit.datasetIndex, hit.index, data) ? 'pointer' : 'default';
    };
    options.plugins.tooltip.callbacks.footer = items => {
      const item = items[0];
      return item && destination(key, item.datasetIndex, item.dataIndex, data) ? 'Bấm để xem chi tiết' : '';
    };
    return options;
  }
  function chartOptions(currency = false) {
    return {
      responsive: true, maintainAspectRatio: false,
      animation: reducedMotion.matches ? false : { duration: 250 },
      interaction: { intersect: false, mode: 'index' },
      plugins: { legend: { display: false }, tooltip: {
        ...tooltipTheme,
        callbacks: { label: item => `${item.dataset.label}: ${number.format(item.parsed.y)}${currency ? ' ₫' : ''}` }
      } },
      scales: {
        x: { grid: { display: false }, border: { display: false }, ticks: { maxTicksLimit: 7, maxRotation: 0, font: { size: 11 }, color: '#000' } },
        y: { beginAtZero: true, border: { display: false }, grid: { color: '#e5e7eb', lineWidth: 1, drawTicks: false },
          ticks: { padding: 10, precision: 0, maxTicksLimit: 5, font: { size: 11 }, color: '#000', callback: value => currency ? compact(value) : number.format(value) } }
      }
    };
  }
  const donutTotal = { id: 'cocoDonutTotal', afterDraw(chart) {
    if (chart.canvas.dataset.chart !== 'orders' || !chart.chartArea) return;
    const { ctx, chartArea: { left, right, top, bottom } } = chart;
    const total = chart.data.datasets[0].data.reduce((sum, value) => sum + value, 0);
    ctx.save(); ctx.textAlign = 'center'; ctx.textBaseline = 'middle'; ctx.fillStyle = '#000';
    ctx.font = '700 28px "Open Sans", sans-serif'; ctx.fillText(number.format(total), (left + right) / 2, (top + bottom) / 2 - 8);
    ctx.fillStyle = '#000'; ctx.font = '400 12px "Open Sans", sans-serif';
    ctx.fillText('đơn hàng', (left + right) / 2, (top + bottom) / 2 + 16); ctx.restore();
  } };
  function updateChart(key, type, data, options, plugins = []) {
    const canvas = root.querySelector(`[data-chart="${key}"]`);
    if (!canvas || !window.Chart) return;
    let chart = chartObjects.get(key);
    if (!chart) { chart = new window.Chart(canvas, { type, data, options, plugins }); chartObjects.set(key, chart); }
    else { chart.data = data; chart.options = options; chart.update(reducedMotion.matches ? 'none' : undefined); }
  }
  function renderCharts(data) {
    if (!window.Chart) return;
    window.Chart.defaults.font.family = '"Open Sans", sans-serif';
    window.Chart.defaults.color = '#000';
    const labels = data.series.map(bucket => bucket.label);
    if (data.canViewFinancials) {
      updateChart('revenue', 'line', { labels, datasets: [
        { label: 'Doanh thu tiền hàng', data: data.series.map(b => b.revenue), borderColor: palette.pinkLine, backgroundColor: palette.pink, fill: false, borderWidth: 2.2, tension: .25, pointRadius: data.series.length > 45 ? 0 : 2, pointHoverRadius: 4, pointHitRadius: 8 },
        { label: 'Hoa hồng CoCo', data: data.series.map(b => b.commission), borderColor: palette.blueLine, borderWidth: 2, tension: .25, pointRadius: data.series.length > 45 ? 0 : 2, pointHoverRadius: 4, pointHitRadius: 8 }
      ] }, drilldownOptions('revenue', data, chartOptions(true)));
    }
    const sales = [];
    if (data.canViewOrders) sales.push({ label: 'Đơn hàng mới', data: data.series.map(b => b.newOrders), backgroundColor: palette.blue, borderRadius: 4, maxBarThickness: 25 });
    sales.push({ label: 'Sản phẩm đã bán', data: data.series.map(b => b.soldProducts), backgroundColor: palette.green, borderRadius: 4, maxBarThickness: 25 });
    updateChart('sales', 'bar', { labels, datasets: sales }, drilldownOptions('sales', data, chartOptions()));
    if (data.canViewOrders) {
      const total = data.orderStatuses.reduce((sum, state) => sum + state.count, 0);
      const canvas = root.querySelector('[data-chart="orders"]');
      canvas.hidden = total === 0; root.querySelector('[data-orders-empty]').hidden = total > 0;
      if (total > 0) updateChart('orders', 'doughnut', {
        labels: data.orderStatuses.map(s => s.label), datasets: [{ data: data.orderStatuses.map(s => s.count),
          backgroundColor: orderColors, borderColor: '#fff', borderWidth: 1, hoverOffset: 4 }]
      }, drilldownOptions('orders', data, { responsive: true, maintainAspectRatio: false, cutout: '66%',
        animation: reducedMotion.matches ? false : { duration: 250 }, plugins: { legend: { display: false }, tooltip: { ...tooltipTheme,
          callbacks: { label: item => `${item.label}: ${number.format(item.parsed)} đơn (${percent.format(item.parsed * 100 / total)}%)` }
        } } }), [donutTotal]);
    }
    const productOptions = chartOptions(); productOptions.indexAxis = 'y';
    productOptions.scales.x = { ...productOptions.scales.y, grid: { color: '#e5e7eb', lineWidth: 1, drawTicks: false } };
    productOptions.scales.y = { border: { display: false }, grid: { display: false }, ticks: { font: { size: 12 }, color: '#000' } };
    productOptions.plugins.tooltip.callbacks.label = item => `${number.format(item.parsed.x)} sản phẩm`;
    updateChart('products', 'bar', { labels: data.productStatuses.map(s => s.label), datasets: [{
      label: 'Sản phẩm', data: data.productStatuses.map(s => s.count), backgroundColor: [palette.pink, palette.green, palette.yellow],
      borderRadius: 5, maxBarThickness: 27
    }] }, drilldownOptions('products', data, productOptions));
  }
  function renderMeasure(element, measure) {
    if (!element) return;
    element.querySelector('[data-measure-value]').textContent = measure.value === null ? '—' : number.format(measure.value) + (measure.currency ? ' ₫' : '');
    const change = element.querySelector('[data-measure-change]');
    let text = 'Không đổi';
    if (measure.value === null) text = 'Chưa được cấp quyền';
    else if (measure.previous === 0 && measure.value > 0) text = 'Mới phát sinh';
    else if (measure.percent !== null && measure.difference !== 0) text = `${measure.percent > 0 ? '+' : ''}${percent.format(measure.percent)}%`;
    change.textContent = text;
    change.parentElement.hidden = measure.value === null || measure.previous === null || measure.difference === 0;
    change.className = 'dashboard-change ' + (measure.favorable ? 'is-favorable' : measure.difference === 0 ? 'is-flat' : 'is-caution');
  }
  function renderTable(data) {
    const body = root.querySelector('[data-dashboard-table]'); body.replaceChildren();
    for (const [index, bucket] of data.series.entries()) {
      const row = document.createElement('tr'), heading = document.createElement('th');
      heading.scope = 'row'; heading.textContent = bucket.label; row.append(heading);
      const cells = [];
      if (data.canViewFinancials) cells.push([number.format(bucket.revenue) + ' ₫', destination('revenue', 0, index, data)], [number.format(bucket.commission) + ' ₫', destination('revenue', 1, index, data)]);
      if (data.canViewOrders) cells.push([number.format(bucket.newOrders), destination('sales', 0, index, data)]);
      cells.push([number.format(bucket.soldProducts), destination('sales', 1, index, data)]);
      for (const [value, url] of cells) {
        const cell = document.createElement('td');
        if (url) { const link = document.createElement('a'); link.href = url; link.textContent = value; link.title = 'Xem chi tiết ' + bucket.label; cell.append(link); }
        else cell.textContent = value;
        row.append(cell);
      }
      body.append(row);
    }
  }
  function legendLink(element, url) {
    if (!element) return;
    let link = element.querySelector(':scope > a[data-chart-link]');
    if (!url) { if (link) element.replaceChildren(...link.childNodes); return; }
    if (!link) {
      link = document.createElement('a'); link.dataset.chartLink = '';
      link.append(...element.childNodes); element.append(link);
    }
    link.href = url; link.title = 'Xem chi tiết ' + element.textContent.trim();
  }
  function render(data) {
    for (const measure of data.metrics) renderMeasure(root.querySelector(`[data-metric="${measure.key}"]`), measure);
    for (const measure of data.business) renderMeasure(root.querySelector(`[data-business="${measure.key}"]`), measure);
    root.querySelector('[data-dashboard-range]').textContent = `${formatDate(data.range.from)} – ${formatDate(data.range.through)}`;
    root.querySelector('[data-dashboard-previous]').textContent = `So với ${formatDate(data.range.previousFrom)} – ${formatDate(data.range.previousThrough)}`;
    const updated = root.querySelector('[data-dashboard-updated]');
    updated.dateTime = data.updatedAtUtc;
    updated.textContent = new Intl.DateTimeFormat('vi-VN', { hour: '2-digit', minute: '2-digit', day: '2-digit', month: '2-digit', year: 'numeric', timeZone: 'Asia/Ho_Chi_Minh' }).format(new Date(data.updatedAtUtc));
    for (const [index, state] of data.orderStatuses.entries()) {
      const row = root.querySelector(`[data-state="${state.key}"]`);
      if (row) { row.querySelector('strong').textContent = number.format(state.count); legendLink(row.querySelector(':scope > span'), destination('orders', 0, index, data)); }
    }
    for (const [index, state] of data.productStatuses.entries()) {
      const row = root.querySelector(`[data-product-state="${state.key}"]`);
      if (row) { row.querySelector('strong').textContent = number.format(state.count); legendLink(row, destination('products', 0, index, data)); }
    }
    renderTable(data); renderCharts(data); latest = data;
  }
  function setCustom() {
    const custom = period.value === 'custom';
    root.querySelector('[data-dashboard-custom]').hidden = !custom;
    from.disabled = through.disabled = !custom;
  }
  function datesValid() {
    through.setCustomValidity('');
    if (period.value === 'custom' && from.value && through.value && from.value > through.value)
      through.setCustomValidity('Ngày kết thúc phải bằng hoặc sau ngày bắt đầu.');
    return form.reportValidity();
  }
  form.addEventListener('submit', async event => {
    event.preventDefault(); if (!datesValid()) return;
    const current = ++revision; request?.abort(); request = new AbortController();
    const params = new URLSearchParams(new FormData(form));
    form.setAttribute('aria-busy', 'true'); form.querySelector('button').disabled = true;
    feedback.hidden = true;
    try {
      const response = await fetch('/api/v1/admin/dashboard?' + params.toString(), { credentials: 'same-origin', cache: 'no-store', signal: request.signal, headers: { Accept: 'application/json' } });
      const data = await response.json().catch(() => null);
      if (!response.ok) throw new Error(data?.error || (response.status === 401 || response.status === 403 ? 'Phiên đăng nhập hoặc quyền truy cập đã thay đổi. Hãy tải lại trang.' : 'Chưa tải được dữ liệu. Các số liệu đang hiển thị được giữ nguyên.'));
      if (current !== revision) return;
      if (latest.canViewOrders !== data.canViewOrders || latest.canViewFinancials !== data.canViewFinancials) { window.location.assign(window.location.pathname + '?' + params.toString()); return; }
      render(data);
      if (period.value !== 'custom') { from.value = data.range.from; through.value = data.range.through; }
      window.history.replaceState(null, '', window.location.pathname + '?' + params.toString());
    } catch (error) {
      if (error.name !== 'AbortError' && current === revision) { feedback.textContent = error.message; feedback.hidden = false; }
    } finally {
      if (current === revision) { form.removeAttribute('aria-busy'); form.querySelector('button').disabled = false; }
    }
  });
  form.addEventListener('change', () => { setCustom(); if (datesValid()) form.requestSubmit(); });
  reducedMotion.addEventListener('change', () => renderCharts(latest));
  setCustom(); render(latest);
  if (!window.Chart) { feedback.textContent = 'Chưa tải được biểu đồ. Bạn vẫn có thể xem các chỉ số và bảng số liệu.'; feedback.hidden = false; }
})();
