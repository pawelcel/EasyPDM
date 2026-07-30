const itemList = document.getElementById('itemList');
const searchInput = document.getElementById('searchInput');
const tagFilter = document.getElementById('tagFilter');
const projectSelect = document.getElementById('projectSelect');
const refreshBtn = document.getElementById('refreshBtn');

const newProjectBtn = document.getElementById('newProjectBtn');
const newProjectPanel = document.getElementById('newProjectPanel');
const newProjectName = document.getElementById('newProjectName');
const newProjectDesc = document.getElementById('newProjectDesc');
const createProjectBtn = document.getElementById('createProjectBtn');
const cancelProjectBtn = document.getElementById('cancelProjectBtn');
const newProjectError = document.getElementById('newProjectError');

const addItemBtn = document.getElementById('addItemBtn');
const addItemPanel = document.getElementById('addItemPanel');
const addItemProjectHint = document.getElementById('addItemProjectHint');
const newItemFile = document.getElementById('newItemFile');
const newItemProps = document.getElementById('newItemProps');
const uploadItemBtn = document.getElementById('uploadItemBtn');
const cancelItemBtn = document.getElementById('cancelItemBtn');
const addItemError = document.getElementById('addItemError');

let openItemId = null;
let projects = [];

// ============================================================
// Projekty
// ============================================================
async function loadProjects() {
  const res = await fetch('/api/projects');
  projects = await res.json();

  const current = projectSelect.value;
  projectSelect.innerHTML = '<option value="">Wszystkie projekty</option>' +
    projects.map(p => `<option value="${p.id}">${escapeHtml(p.name)} (${p.itemCount})</option>`).join('');
  projectSelect.value = projects.some(p => p.id === current) ? current : '';
}

newProjectBtn.addEventListener('click', () => {
  addItemPanel.classList.add('hidden');
  newProjectPanel.classList.toggle('hidden');
  newProjectError.classList.add('hidden');
  newProjectName.value = '';
  newProjectDesc.value = '';
  newProjectName.focus();
});

cancelProjectBtn.addEventListener('click', () => newProjectPanel.classList.add('hidden'));

createProjectBtn.addEventListener('click', async () => {
  const name = newProjectName.value.trim();
  if (!name) {
    showError(newProjectError, 'Nazwa projektu jest wymagana.');
    return;
  }

  const res = await fetch('/api/projects', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name, description: newProjectDesc.value.trim() || null })
  });

  if (res.status === 409) {
    showError(newProjectError, 'Projekt o tej nazwie już istnieje.');
    return;
  }
  if (!res.ok) {
    showError(newProjectError, 'Nie udało się utworzyć projektu.');
    return;
  }

  const created = await res.json();
  newProjectPanel.classList.add('hidden');
  await loadProjects();
  projectSelect.value = created.id;
  await loadItems();
});

// ============================================================
// Dodawanie elementu (upload pliku)
// ============================================================
addItemBtn.addEventListener('click', () => {
  if (!projectSelect.value) {
    alert('Najpierw wybierz projekt z listy (albo utwórz nowy przyciskiem "+ Projekt").');
    return;
  }
  newProjectPanel.classList.add('hidden');
  addItemPanel.classList.toggle('hidden');
  addItemError.classList.add('hidden');
  const project = projects.find(p => p.id === projectSelect.value);
  addItemProjectHint.textContent = `Projekt: ${project ? project.name : ''}`;
  newItemFile.value = '';
  newItemProps.value = '';
});

cancelItemBtn.addEventListener('click', () => addItemPanel.classList.add('hidden'));

uploadItemBtn.addEventListener('click', async () => {
  const projectId = projectSelect.value;
  const file = newItemFile.files[0];
  if (!file) {
    showError(addItemError, 'Wybierz plik.');
    return;
  }

  const propsText = newItemProps.value.trim();
  if (propsText) {
    try { JSON.parse(propsText); }
    catch { showError(addItemError, 'Pole właściwości musi być poprawnym JSON-em, np. {"material":"Stal S235"}'); return; }
  }

  const formData = new FormData();
  formData.append('file', file);
  if (propsText) formData.append('properties', propsText);

  uploadItemBtn.disabled = true;
  uploadItemBtn.textContent = 'Wgrywanie…';

  const res = await fetch(`/api/projects/${projectId}/items`, { method: 'POST', body: formData });

  uploadItemBtn.disabled = false;
  uploadItemBtn.textContent = 'Wgraj';

  if (!res.ok) {
    const text = await res.text();
    showError(addItemError, text || 'Nie udało się wgrać elementu.');
    return;
  }

  addItemPanel.classList.add('hidden');
  await loadProjects();
  await loadItems();
});

// ============================================================
// Elementy
// ============================================================
async function loadTags() {
  const res = await fetch('/api/tags');
  const tags = await res.json();
  const current = tagFilter.value;
  tagFilter.innerHTML = '<option value="">Wszystkie tagi</option>' +
    tags.map(t => `<option value="${escapeHtml(t)}">${escapeHtml(t)}</option>`).join('');
  tagFilter.value = current;
}

async function loadItems() {
  const params = new URLSearchParams();
  if (searchInput.value.trim()) params.set('search', searchInput.value.trim());
  if (tagFilter.value) params.set('tag', tagFilter.value);
  if (projectSelect.value) params.set('projectId', projectSelect.value);

  itemList.innerHTML = '<p class="hint">Ładowanie…</p>';
  const res = await fetch('/api/items?' + params.toString());
  if (!res.ok) {
    itemList.innerHTML = '<p class="hint">Błąd ładowania danych z API.</p>';
    return;
  }
  const items = await res.json();
  render(items);
}

function render(items) {
  if (items.length === 0) {
    itemList.innerHTML = '<p class="hint">Brak elementów pasujących do filtra. Kliknij "+ Element", żeby dodać pierwszy.</p>';
    return;
  }

  itemList.innerHTML = items.map(item => renderItem(item)).join('');

  items.forEach(item => {
    document.getElementById(`header-${item.id}`).addEventListener('click', () => toggleDetails(item.id));
  });

  if (openItemId) {
    const details = document.getElementById(`details-${openItemId}`);
    if (details) details.classList.add('open');
  }
}

function renderItem(item) {
  const modified = item.modifiedAt ? new Date(item.modifiedAt).toLocaleString('pl-PL') : '—';
  const propsRows = Object.entries(item.properties || {})
    .map(([k, v]) => `<tr><td>${escapeHtml(k)}</td><td>${escapeHtml(String(v))}</td></tr>`)
    .join('');

  const tagsHtml = (item.tags || [])
    .map(t => `<span class="tag">${escapeHtml(t)} <button onclick="removeTag('${item.id}','${escapeAttr(t)}')">×</button></span>`)
    .join('');

  const project = projects.find(p => p.id === item.projectId);

  return `
    <div class="item-card">
      <div class="item-header" id="header-${item.id}">
        <div>
          <div class="item-name">${escapeHtml(item.fileName)}</div>
          <div class="item-meta">${escapeHtml(item.fileType.toUpperCase())} · zmodyfikowano ${modified}${project ? ' · ' + escapeHtml(project.name) : ''}</div>
        </div>
      </div>
      <div class="item-details" id="details-${item.id}">
        <a class="file-link" href="/api/items/${item.id}/file" download>⬇ Pobierz plik</a>

        <div class="section-label">Tagi</div>
        <div class="tags">${tagsHtml || '<span class="item-meta">brak tagów</span>'}</div>
        <div class="add-tag-row">
          <input type="text" placeholder="nowy tag…" id="tagInput-${item.id}"
                 onkeydown="if(event.key==='Enter') addTag('${item.id}')" />
          <button onclick="addTag('${item.id}')">Dodaj</button>
        </div>

        <div class="section-label">Właściwości</div>
        ${propsRows ? `<table class="props">${propsRows}</table>` : '<p class="item-meta">brak właściwości</p>'}
      </div>
    </div>
  `;
}

function toggleDetails(id) {
  const details = document.getElementById(`details-${id}`);
  const isOpen = details.classList.contains('open');
  document.querySelectorAll('.item-details.open').forEach(el => el.classList.remove('open'));
  if (!isOpen) { details.classList.add('open'); openItemId = id; }
  else { openItemId = null; }
}

async function addTag(itemId) {
  const input = document.getElementById(`tagInput-${itemId}`);
  const name = input.value.trim();
  if (!name) return;

  openItemId = itemId;
  await fetch(`/api/items/${itemId}/tags`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name })
  });
  await loadTags();
  await loadItems();
}

async function removeTag(itemId, tagName) {
  openItemId = itemId;
  await fetch(`/api/items/${itemId}/tags/${encodeURIComponent(tagName)}`, { method: 'DELETE' });
  await loadItems();
}

function showError(el, message) {
  el.textContent = message;
  el.classList.remove('hidden');
}

function escapeHtml(str) {
  const div = document.createElement('div');
  div.textContent = str;
  return div.innerHTML;
}
function escapeAttr(str) { return String(str).replace(/'/g, "\\'"); }

searchInput.addEventListener('input', debounce(loadItems, 300));
tagFilter.addEventListener('change', loadItems);
projectSelect.addEventListener('change', loadItems);
refreshBtn.addEventListener('click', () => { loadProjects(); loadTags(); loadItems(); });

function debounce(fn, delay) {
  let timer;
  return (...args) => { clearTimeout(timer); timer = setTimeout(() => fn(...args), delay); };
}

(async () => {
  await loadProjects();
  await loadTags();
  await loadItems();
})();
