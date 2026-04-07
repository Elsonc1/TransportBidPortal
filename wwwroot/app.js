let token = "";
let userRole = "";
let selectedBidDeadline = null;
let priceChart;
let deviationChart;
let heatmapChart;
let adminProfiles = [];
let logCurrentPage = 1;
let sidebarPinned = false;
/** Base da API; vazio = mesmo host (browser ou Capacitor com `server.url` apontando para o backend). */
const API_BASE = (() => {
  if (window.location.protocol === "file:") return "http://localhost:5157";
  try {
    if (window.Capacitor?.isNativePlatform?.()) {
      const b = window.__TRANSPORT_BID_API_BASE__;
      if (typeof b === "string" && b.trim()) return b.replace(/\/$/, "");
    }
  } catch (_) {}
  return "";
})();

function generateCorrelationId() {
  return crypto.randomUUID ? crypto.randomUUID().replace(/-/g, "").slice(0, 12) : Date.now().toString(36) + Math.random().toString(36).slice(2, 8);
}

function apiUrl(path) {
  return `${API_BASE}${path}`;
}

function authHeaders() {
  return { Authorization: `Bearer ${token}`, "X-Correlation-Id": generateCorrelationId() };
}

function escapeHtml(s) {
  return String(s)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/"/g, "&quot;");
}

function applyMobileTableLabels(tableSelector) {
  if (!tableSelector) return;
  const table = document.querySelector(tableSelector);
  if (!table) return;
  const headers = [...table.querySelectorAll("thead th")].map(th => (th.textContent || "").trim());
  table.querySelectorAll("tbody tr").forEach(tr => {
    [...tr.children].forEach((td, idx) => {
      if (td.tagName !== "TD") return;
      const label = headers[idx] || "";
      if (label) td.setAttribute("data-label", label);
    });
  });
}

/** Select de Origem na grid de lanes: apenas CDs ativos do shipper (`facilitiesCache`). */
function bidOriginFacilitiesList() {
  return (facilitiesCache || []).filter(f => f.isActive && (f.type === "Matriz" || f.type === "Filial"));
}

function bidOriginOptionsHtml(preselectedId) {
  const list = bidOriginFacilitiesList();
  const head = `<option value="">-- selecione o CD --</option>`;
  const body = list
    .map(f => {
      const sel =
        preselectedId != null && preselectedId !== "" && String(f.id) === String(preselectedId)
          ? " selected"
          : "";
      const label = `${f.name} (${f.city}/${f.state}) — ${f.type}`;
      return `<option value="${f.id}"${sel}>${escapeHtml(label)}</option>`;
    })
    .join("");
  return head + body;
}

function bidOriginFacilitySelectHtml(preselectedId) {
  return `<select data-key="Origin" class="bid-lane-origin-select" style="width:100%;padding:5px 8px;border:1px solid #d6e0f0;border-radius:3px;font-size:13px">${bidOriginOptionsHtml(preselectedId)}</select>`;
}

function bidSyncLaneOriginSelects(forceOriginId) {
  const selects = [...document.querySelectorAll('#bidLanesBody select[data-key="Origin"]')];
  if (!selects.length) return;

  selects.forEach(sel => {
    const current = sel.value || "";
    const nextSelected = forceOriginId || current;
    sel.innerHTML = bidOriginOptionsHtml(nextSelected);
  });
}

let deliveryPointsCache = [];

function bidDeliveryPointsList() {
  return (deliveryPointsCache || []).filter(p => p.isActive);
}

function bidDestinationOptionsHtml(preselectedId) {
  const list = bidDeliveryPointsList();
  const head = `<option value="">-- selecione o ponto --</option>`;
  const body = list
    .map(p => {
      const sel =
        preselectedId != null && preselectedId !== "" && String(p.id) === String(preselectedId) ? " selected" : "";
      const label = `${p.name} (${p.city}/${p.state})`;
      return `<option value="${p.id}"${sel}>${escapeHtml(label)}</option>`;
    })
    .join("");
  return head + body;
}

function bidDestinationDeliveryPointSelectHtml(preselectedId) {
  return `<select data-key="Destination" class="bid-lane-dest-select" style="width:100%;padding:5px 8px;border:1px solid #d6e0f0;border-radius:3px;font-size:13px">${bidDestinationOptionsHtml(preselectedId)}</select>`;
}

function brazilMacroRegionFromUf(uf) {
  const u = (uf || "").trim().toUpperCase();
  if (u.length !== 2) return "";
  const N = ["AC", "AM", "AP", "PA", "RO", "RR", "TO"];
  const NE = ["AL", "BA", "CE", "MA", "PB", "PE", "PI", "RN", "SE"];
  const CO = ["DF", "GO", "MT", "MS"];
  const SE = ["ES", "MG", "RJ", "SP"];
  const S = ["PR", "RS", "SC"];
  if (N.includes(u)) return "N";
  if (NE.includes(u)) return "NE";
  if (CO.includes(u)) return "CO";
  if (SE.includes(u)) return "SE";
  if (S.includes(u)) return "S";
  return "";
}

/** Região do cadastro ou macro-região derivada da UF (espelha BrazilMacroRegion no backend). */
function bidEffectiveRegionForPoint(p) {
  const r = (p.region || "").trim();
  if (r) return r;
  return brazilMacroRegionFromUf(p.state);
}

function bidRefreshDestinationSelectOptions() {
  document.querySelectorAll('#bidLanesBody select[data-key="Destination"]').forEach(sel => {
    const cur = sel.value;
    sel.innerHTML = bidDestinationOptionsHtml(cur);
    const tr = sel.closest("tr");
    const regionInput = tr?.querySelector('[data-key="Region"]');
    if (regionInput && cur) {
      const pt = deliveryPointsCache.find(x => String(x.id) === String(cur));
      if (pt) regionInput.value = bidEffectiveRegionForPoint(pt);
    }
  });
}

function bidLanesBodyOnChangeDest(e) {
  const sel = e.target;
  if (sel.tagName !== "SELECT" || sel.dataset.key !== "Destination") return;
  const tr = sel.closest("tr");
  const regionInput = tr?.querySelector('[data-key="Region"]');
  if (!regionInput) return;
  const id = sel.value;
  if (!id) {
    regionInput.value = "";
    return;
  }
  const p = deliveryPointsCache.find(x => String(x.id) === String(id));
  regionInput.value = p ? bidEffectiveRegionForPoint(p) : "";
}

function setDefaultDeadline() {
  const now = new Date(Date.now() + 86400000);
  document.getElementById("deadline").value = now.toISOString().slice(0, 16);
}

/* ---------- SIDEBAR ---------- */

const SIDEBAR_MENUS = {
  Shipper: [
    { id: "facilities", icon: "&#x1F3E2;", label: "Unidades (CDs)" },
    { id: "delivery-points", icon: "&#x1F4E6;", label: "Pontos de entrega" },
    { id: "bid-create", icon: "&#x1F4CB;", label: "Criar BID" },
    { id: "template-studio", icon: "&#x1F527;", label: "Template Studio" },
    { divider: true },
    { id: "dashboard", icon: "&#x1F4C8;", label: "Dashboard & Ranking" }
  ],
  Carrier: [
    { id: "carrier-bids", icon: "&#x1F4E6;", label: "BIDs & Propostas" }
  ],
  Admin: [
    { id: "admin-profiles", icon: "&#x2699;", label: "Perfis Mapeamento" },
    { id: "admin-logs", icon: "&#x1F4DD;", label: "System Logs" }
  ]
};

function sidebarBuild(role) {
  const nav = document.getElementById("sidebarNav");
  nav.innerHTML = "";
  const items = SIDEBAR_MENUS[role] || [];
  items.forEach(item => {
    if (item.divider) {
      const hr = document.createElement("div");
      hr.className = "sb-divider";
      nav.appendChild(hr);
      return;
    }
    const div = document.createElement("div");
    div.className = "sb-item";
    div.dataset.page = item.id;
    div.innerHTML = `<span class="sb-icon">${item.icon}</span><span class="sidebar__label">${item.label}</span>`;
    div.addEventListener("click", () => navigateTo(item.id));
    nav.appendChild(div);
  });
}

function navigateTo(pageId) {
  document.querySelectorAll(".page-section").forEach(s => s.classList.add("hidden"));
  const target = document.querySelector(`.page-section[data-page="${pageId}"]`);
  if (target) target.classList.remove("hidden");

  document.querySelectorAll(".sb-item").forEach(el => {
    el.classList.toggle("sb-item--active", el.dataset.page === pageId);
  });

  if (isMobileViewport()) {
    closeMobileSidebar();
  }
}

function sidebarToggle() {
  const sb = document.getElementById("sidebar");
  if (isMobileViewport()) {
    const open = sb.classList.contains("sidebar--mobile-open");
    if (open) closeMobileSidebar();
    else openMobileSidebar();
    return;
  }
  if (sidebarPinned) {
    sidebarPinned = false;
    sb.classList.remove("sidebar--pinned");
    sb.classList.add("sidebar--collapsed");
  } else {
    sidebarPinned = true;
    sb.classList.remove("sidebar--collapsed");
    sb.classList.add("sidebar--pinned");
  }
}

function sidebarPin() {
  if (isMobileViewport()) return;
  sidebarPinned = !sidebarPinned;
  const sb = document.getElementById("sidebar");
  if (sidebarPinned) {
    sb.classList.remove("sidebar--collapsed");
    sb.classList.add("sidebar--pinned");
  } else {
    sb.classList.add("sidebar--collapsed");
    sb.classList.remove("sidebar--pinned");
  }
}

function isMobileViewport() {
  return window.matchMedia("(max-width: 1024px)").matches;
}

function openMobileSidebar() {
  const sb = document.getElementById("sidebar");
  const backdrop = document.getElementById("sidebarBackdrop");
  if (!sb) return;
  sb.classList.add("sidebar--mobile-open");
  if (backdrop) backdrop.classList.remove("hidden");
}

function closeMobileSidebar() {
  const sb = document.getElementById("sidebar");
  const backdrop = document.getElementById("sidebarBackdrop");
  if (!sb) return;
  sb.classList.remove("sidebar--mobile-open");
  if (backdrop) backdrop.classList.add("hidden");
}

function syncSidebarByViewport() {
  const sb = document.getElementById("sidebar");
  const pinBtn = document.getElementById("sidebarPinBtn");
  if (!sb) return;
  if (isMobileViewport()) {
    closeMobileSidebar();
    sb.classList.remove("sidebar--pinned");
    sb.classList.add("sidebar--collapsed");
    if (pinBtn) pinBtn.classList.add("hidden");
    sidebarPinned = false;
  } else {
    if (pinBtn) pinBtn.classList.remove("hidden");
  }
}

/* ---------- LOGIN ---------- */

async function login() {
  const email = document.getElementById("email").value;
  const password = document.getElementById("password").value;
  const res = await fetch(apiUrl("/api/auth/login"), {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Correlation-Id": generateCorrelationId() },
    body: JSON.stringify({ email, password })
  });
  if (!res.ok) return alert("Login failed.");

  const data = await res.json();
  token = data.token;
  userRole = data.role;
  document.getElementById("authWrapper").classList.add("hidden");
  document.getElementById("appShell").classList.remove("hidden");
  document.getElementById("sidebarToggleBtn").classList.remove("hidden");
  document.getElementById("userChip").textContent = `${data.name} (${data.role})`;

  sidebarBuild(userRole);

  if (userRole === "Shipper") {
    navigateTo("facilities");
    await loadFacilities();
    await loadDeliveryPoints();
    bidPopulateOriginSelect();
    await loadCarriers();
    await loadTemplates();
    bidPopulateTemplateSelect();
    await loadBids();
    await loadAudit();
  } else if (userRole === "Admin") {
    navigateTo("admin-profiles");
    await loadAdminShippers();
    await loadAdminProfiles();
    await loadLogServices();
    await searchLogs();
  } else {
    navigateTo("carrier-bids");
    await loadInvitedBids();
    await loadNotifications();
  }
}

/* ---------- FACILITIES (Matriz / Filiais) ---------- */

let facilitiesCache = [];

async function loadFacilities() {
  const res = await fetch(apiUrl("/api/shipper/facilities"), { headers: authHeaders() });
  if (!res.ok) return;
  facilitiesCache = await res.json();
  renderFacilityGrid();
  bidPopulateOriginSelect();
}

function renderFacilityGrid() {
  const tbody = document.getElementById("facilityGridBody");
  if (!facilitiesCache.length) {
    tbody.innerHTML = `<tr><td colspan="9" style="text-align:center;color:#8a9ab8;padding:20px">Nenhuma unidade cadastrada. Clique em "Nova Unidade" para começar.</td></tr>`;
    return;
  }
  tbody.innerHTML = facilitiesCache.map(f => {
    const typeCls = f.type === "Matriz" ? "facility-badge--matriz" : "facility-badge--filial";
    const activeCls = f.isActive ? "facility-active" : "facility-inactive";
    return `<tr>
      <td><span class="facility-badge ${typeCls}">${f.type}</span></td>
      <td><strong>${f.name}</strong></td>
      <td style="font-family:monospace;font-size:12px">${f.cnpj}</td>
      <td>${f.address}</td>
      <td>${f.city}</td>
      <td>${f.state}</td>
      <td>${f.zipCode}</td>
      <td class="studio-td--center"><span class="${activeCls}">${f.isActive ? "Sim" : "Não"}</span></td>
      <td class="studio-td--actions">
        <button class="studio-btn studio-btn--outline" style="padding:4px 8px;font-size:12px" onclick="facilityEdit('${f.id}')">Editar</button>
        <button class="studio-btn--danger" onclick="facilityDelete('${f.id}','${f.name}')">&#x2716;</button>
      </td>
    </tr>`;
  }).join("");
  applyMobileTableLabels("#facilityGrid");
}

function facilityShowForm() {
  document.getElementById("facilityEditId").value = "";
  document.getElementById("facilityType").value = "Filial";
  document.getElementById("facilityName").value = "";
  document.getElementById("facilityCnpj").value = "";
  document.getElementById("facilityAddress").value = "";
  document.getElementById("facilityCity").value = "";
  document.getElementById("facilityState").value = "";
  document.getElementById("facilityZip").value = "";
  document.getElementById("facilityCnpjHint").textContent = "";
  document.getElementById("facilityForm").classList.remove("hidden");
  document.getElementById("facilityCnpj").focus();
}

function cnpjExtractBase(cnpj) {
  return cnpj.replace(/\D/g, "").slice(0, 8);
}

function cnpjExtractBranch(cnpj) {
  return cnpj.replace(/\D/g, "").slice(8, 12);
}

function facilityCnpjCheck() {
  const cnpj = document.getElementById("facilityCnpj").value;
  const digits = cnpj.replace(/\D/g, "");
  const hint = document.getElementById("facilityCnpjHint");
  const typeSelect = document.getElementById("facilityType");
  const editId = document.getElementById("facilityEditId").value;

  if (digits.length < 14) {
    hint.textContent = "";
    return;
  }

  const base = cnpjExtractBase(cnpj);
  const branch = cnpjExtractBranch(cnpj);
  const isMatriz = branch === "0001";

  typeSelect.value = isMatriz ? "Matriz" : "Filial";

  if (isMatriz) {
    const existing = facilitiesCache.find(f => cnpjExtractBase(f.cnpj) === base && f.type === "Matriz" && f.id !== editId);
    if (existing) {
      hint.textContent = `Matriz j\u00e1 cadastrada: ${existing.name}. Altere o sufixo para registrar filial.`;
      hint.style.color = "#c0392b";
    } else {
      hint.textContent = "CNPJ de matriz (/0001). Ser\u00e1 cadastrada como Matriz.";
      hint.style.color = "#1a8754";
    }
  } else {
    const matriz = facilitiesCache.find(f => cnpjExtractBase(f.cnpj) === base && f.type === "Matriz");
    if (matriz) {
      hint.textContent = `Filial vinculada \u00e0 matriz: ${matriz.name} (${matriz.cnpj})`;
      hint.style.color = "#1a8754";
    } else {
      hint.textContent = `Nenhuma matriz com raiz ${base} cadastrada. Cadastre a matriz primeiro (/0001).`;
      hint.style.color = "#c0392b";
    }
  }
}

function facilityCancelForm() {
  document.getElementById("facilityForm").classList.add("hidden");
}

async function facilityLookupCep() {
  const cep = document.getElementById("facilityZip").value.replace(/\D/g, "");
  const hint = document.getElementById("facilityCepHint");
  if (cep.length !== 8) {
    hint.textContent = "CEP deve ter 8 d\u00edgitos.";
    hint.style.color = "#c0392b";
    return;
  }
  hint.textContent = "Buscando...";
  hint.style.color = "#8a9ab8";
  try {
    const res = await fetch(apiUrl(`/api/cep/${cep}`), { headers: authHeaders() });
    if (!res.ok) {
      hint.textContent = "CEP n\u00e3o encontrado.";
      hint.style.color = "#c0392b";
      return;
    }
    const data = await res.json();
    document.getElementById("facilityAddress").value = [data.logradouro, data.complemento].filter(Boolean).join(", ");
    document.getElementById("facilityCity").value = data.localidade;
    document.getElementById("facilityState").value = data.uf;
    hint.textContent = `${data.localidade} - ${data.uf} (${data.bairro})`;
    hint.style.color = "#1a8754";
  } catch {
    hint.textContent = "Erro ao consultar CEP.";
    hint.style.color = "#c0392b";
  }
}

function facilityEdit(id) {
  const f = facilitiesCache.find(x => x.id === id);
  if (!f) return;
  document.getElementById("facilityEditId").value = f.id;
  document.getElementById("facilityType").value = f.type;
  document.getElementById("facilityName").value = f.name;
  document.getElementById("facilityCnpj").value = f.cnpj;
  document.getElementById("facilityAddress").value = f.address;
  document.getElementById("facilityCity").value = f.city;
  document.getElementById("facilityState").value = f.state;
  document.getElementById("facilityZip").value = f.zipCode;
  document.getElementById("facilityForm").classList.remove("hidden");
  facilityCnpjCheck();
  document.getElementById("facilityName").focus();
}

async function facilitySave() {
  const editId = document.getElementById("facilityEditId").value;
  const cnpjRaw = document.getElementById("facilityCnpj").value;
  const digits = cnpjRaw.replace(/\D/g, "");

  if (digits.length < 14) {
    return alert("Informe um CNPJ v\u00e1lido com 14 d\u00edgitos.");
  }

  const base = cnpjExtractBase(cnpjRaw);
  const branch = cnpjExtractBranch(cnpjRaw);
  const isMatriz = branch === "0001";
  const detectedType = isMatriz ? "Matriz" : "Filial";

  if (isMatriz) {
    const existing = facilitiesCache.find(f => cnpjExtractBase(f.cnpj) === base && f.type === "Matriz" && f.id !== editId);
    if (existing) {
      return alert(`J\u00e1 existe uma matriz com esta raiz de CNPJ: ${existing.name} (${existing.cnpj}). Use um sufixo diferente de /0001 para cadastrar filial.`);
    }
  } else {
    const matriz = facilitiesCache.find(f => cnpjExtractBase(f.cnpj) === base && f.type === "Matriz");
    if (!matriz) {
      return alert(`Para cadastrar uma filial, primeiro cadastre a matriz com CNPJ raiz ${base}/0001-XX.`);
    }
  }

  const payload = {
    type: detectedType,
    name: document.getElementById("facilityName").value,
    cnpj: cnpjRaw,
    address: document.getElementById("facilityAddress").value,
    city: document.getElementById("facilityCity").value,
    state: document.getElementById("facilityState").value,
    zipCode: document.getElementById("facilityZip").value,
    country: "Brasil",
    latitude: null,
    longitude: null,
    isActive: true
  };

  if (!payload.name || !payload.city || !payload.state) {
    return alert("Preencha ao menos Nome, Cidade e UF.");
  }

  const endpoint = editId
    ? `/api/shipper/facilities/${editId}`
    : "/api/shipper/facilities";
  const method = editId ? "PUT" : "POST";

  const res = await fetch(apiUrl(endpoint), {
    method,
    headers: { ...authHeaders(), "Content-Type": "application/json" },
    body: JSON.stringify(payload)
  });
  if (!res.ok) return alert(await res.text());

  facilityCancelForm();
  await loadFacilities();
}

async function facilityDelete(id, name) {
  if (!confirm(`Remover a unidade "${name}"?`)) return;
  const res = await fetch(apiUrl(`/api/shipper/facilities/${id}`), {
    method: "DELETE",
    headers: authHeaders()
  });
  if (!res.ok) return alert(await res.text());
  await loadFacilities();
}

/* ---------- PONTOS DE ENTREGA (destinos BID) ---------- */

async function loadDeliveryPoints() {
  const res = await fetch(apiUrl("/api/shipper/delivery-points"), { headers: authHeaders() });
  if (!res.ok) return;
  deliveryPointsCache = await res.json();
  renderDeliveryPointGrid();
  bidRefreshDestinationSelectOptions();
}

function renderDeliveryPointGrid() {
  const tbody = document.getElementById("deliveryPointGridBody");
  if (!tbody) return;
  if (!deliveryPointsCache.length) {
    tbody.innerHTML = `<tr><td colspan="9" style="text-align:center;color:#8a9ab8;padding:20px">Nenhum ponto cadastrado. Use &quot;Novo ponto&quot; para incluir destinos de entrega.</td></tr>`;
    return;
  }
  tbody.innerHTML = deliveryPointsCache
    .map(p => {
      const activeCls = p.isActive ? "facility-active" : "facility-inactive";
      const reg = (p.region || "").trim() || brazilMacroRegionFromUf(p.state) || "—";
      return `<tr>
      <td><strong>${escapeHtml(p.name)}</strong></td>
      <td>${escapeHtml(p.address || "")}</td>
      <td>${escapeHtml(p.city)}</td>
      <td>${escapeHtml(p.state)}</td>
      <td style="font-family:monospace;font-size:12px">${escapeHtml(p.zipCode || "")}</td>
      <td>${escapeHtml(reg)}</td>
      <td class="studio-td--center"><span class="${activeCls}">${p.isActive ? "Sim" : "N\u00e3o"}</span></td>
      <td class="studio-td--center" style="font-size:11px;color:#6c7a8f">${p.latitude != null ? `${p.latitude}, ${p.longitude}` : "—"}</td>
      <td class="studio-td--actions">
        <button class="studio-btn studio-btn--outline" style="padding:4px 8px;font-size:12px" onclick="deliveryPointEdit('${p.id}')">Editar</button>
        <button class="studio-btn--danger" onclick="deliveryPointDelete('${p.id}')">&#x2716;</button>
      </td>
    </tr>`;
    })
    .join("");
  applyMobileTableLabels("#deliveryPointGrid");
}

function deliveryPointShowForm() {
  document.getElementById("dpEditId").value = "";
  document.getElementById("dpName").value = "";
  document.getElementById("dpAddress").value = "";
  document.getElementById("dpCity").value = "";
  document.getElementById("dpState").value = "";
  document.getElementById("dpZip").value = "";
  document.getElementById("dpRegion").value = "";
  document.getElementById("dpCepHint").textContent = "";
  const dpAct = document.getElementById("dpIsActive");
  if (dpAct) dpAct.checked = true;
  document.getElementById("dpForm").classList.remove("hidden");
  document.getElementById("dpName").focus();
}

function deliveryPointCancelForm() {
  document.getElementById("dpForm").classList.add("hidden");
}

async function deliveryPointLookupCep() {
  const cep = document.getElementById("dpZip").value.replace(/\D/g, "");
  const hint = document.getElementById("dpCepHint");
  if (cep.length !== 8) {
    hint.textContent = "CEP deve ter 8 d\u00edgitos.";
    hint.style.color = "#c0392b";
    return;
  }
  hint.textContent = "Buscando...";
  hint.style.color = "#8a9ab8";
  try {
    const res = await fetch(apiUrl(`/api/cep/${cep}`), { headers: authHeaders() });
    if (!res.ok) {
      hint.textContent = "CEP n\u00e3o encontrado.";
      hint.style.color = "#c0392b";
      return;
    }
    const data = await res.json();
    document.getElementById("dpAddress").value = [data.logradouro, data.complemento].filter(Boolean).join(", ");
    document.getElementById("dpCity").value = data.localidade;
    document.getElementById("dpState").value = data.uf;
    hint.textContent = `${data.localidade} - ${data.uf} (${data.bairro})`;
    hint.style.color = "#1a8754";
  } catch {
    hint.textContent = "Erro ao consultar CEP.";
    hint.style.color = "#c0392b";
  }
}

function deliveryPointEdit(id) {
  const p = deliveryPointsCache.find(x => x.id === id);
  if (!p) return;
  document.getElementById("dpEditId").value = p.id;
  document.getElementById("dpName").value = p.name;
  document.getElementById("dpAddress").value = p.address || "";
  document.getElementById("dpCity").value = p.city;
  document.getElementById("dpState").value = p.state;
  document.getElementById("dpZip").value = p.zipCode || "";
  document.getElementById("dpRegion").value = p.region || "";
  const dpAct = document.getElementById("dpIsActive");
  if (dpAct) dpAct.checked = !!p.isActive;
  document.getElementById("dpForm").classList.remove("hidden");
  document.getElementById("dpName").focus();
}

async function deliveryPointSave() {
  const editId = document.getElementById("dpEditId").value;
  const payload = {
    name: document.getElementById("dpName").value,
    address: document.getElementById("dpAddress").value,
    city: document.getElementById("dpCity").value,
    state: document.getElementById("dpState").value,
    zipCode: document.getElementById("dpZip").value,
    region: document.getElementById("dpRegion").value || null,
    country: "Brasil",
    latitude: null,
    longitude: null,
    isActive: document.getElementById("dpIsActive") ? document.getElementById("dpIsActive").checked : true
  };
  if (!payload.name || !payload.city || !payload.state) {
    return alert("Preencha nome, cidade e UF.");
  }
  const endpoint = editId ? `/api/shipper/delivery-points/${editId}` : "/api/shipper/delivery-points";
  const method = editId ? "PUT" : "POST";
  const res = await fetch(apiUrl(endpoint), {
    method,
    headers: { ...authHeaders(), "Content-Type": "application/json" },
    body: JSON.stringify(payload)
  });
  if (!res.ok) return alert(await res.text());
  deliveryPointCancelForm();
  await loadDeliveryPoints();
}

async function deliveryPointDelete(id) {
  const p = deliveryPointsCache.find(x => x.id === id);
  const name = p ? p.name : "este ponto";
  if (!confirm(`Remover o ponto "${name}"?`)) return;
  const res = await fetch(apiUrl(`/api/shipper/delivery-points/${id}`), {
    method: "DELETE",
    headers: authHeaders()
  });
  if (!res.ok) return alert(await res.text());
  await loadDeliveryPoints();
}

/* ---------- BID TEMPLATE STUDIO (NDD-inspired grid) ---------- */

const STUDIO_DEFAULT_ROWS = [
  { key: "Origin", displayName: "Origem", aliases: "origem,origin,cidade origem", isRequired: true, dataType: "text" },
  { key: "Destination", displayName: "Destino", aliases: "destino,destination,cidade destino", isRequired: true, dataType: "text" },
  { key: "FreightType", displayName: "Tipo Frete", aliases: "tipo frete,freight type", isRequired: false, dataType: "text" },
  { key: "VolumeForecast", displayName: "Volume", aliases: "volume,previsao volume,forecast", isRequired: false, dataType: "number" },
  { key: "VehicleType", displayName: "Tipo Veículo", aliases: "tipo veiculo,vehicle type", isRequired: false, dataType: "text" },
  { key: "SlaRequirements", displayName: "SLA", aliases: "sla,service level", isRequired: false, dataType: "text" },
  { key: "Region", displayName: "Região", aliases: "regiao,region", isRequired: false, dataType: "text" }
];

let studioDragSrcRow = null;

function studioRenderGrid(rows) {
  const tbody = document.getElementById("studioGridBody");
  tbody.innerHTML = "";
  (rows || STUDIO_DEFAULT_ROWS).forEach((r, i) => {
    const tr = document.createElement("tr");
    tr.draggable = true;
    tr.dataset.idx = i;
    tr.addEventListener("dragstart", studioOnDragStart);
    tr.addEventListener("dragover", studioOnDragOver);
    tr.addEventListener("drop", studioOnDrop);
    tr.addEventListener("dragend", studioOnDragEnd);
    tr.innerHTML = `
      <td class="studio-td--center"><button class="studio-btn--move" title="Arrastar">&#x2630;</button><span class="studio-grid__order-num">${i + 1}</span></td>
      <td><input type="text" value="${r.key || ""}" data-field="key" /></td>
      <td><input type="text" value="${r.displayName || ""}" data-field="displayName" /></td>
      <td><input type="text" value="${r.aliases || ""}" data-field="aliases" /></td>
      <td class="studio-td--center"><input type="checkbox" data-field="isRequired" ${r.isRequired ? "checked" : ""} /></td>
      <td><select data-field="dataType">
        <option value="text" ${r.dataType === "text" ? "selected" : ""}>text</option>
        <option value="number" ${r.dataType === "number" ? "selected" : ""}>number</option>
        <option value="date" ${r.dataType === "date" ? "selected" : ""}>date</option>
        <option value="currency" ${r.dataType === "currency" ? "selected" : ""}>currency</option>
      </select></td>
      <td class="studio-td--actions">
        <button class="studio-btn--danger" title="Remover" onclick="studioRemoveRow(this)">&#x2716;</button>
      </td>`;
    tbody.appendChild(tr);
  });
}

function studioAddRow() {
  const tbody = document.getElementById("studioGridBody");
  const idx = tbody.rows.length;
  const tr = document.createElement("tr");
  tr.draggable = true;
  tr.dataset.idx = idx;
  tr.addEventListener("dragstart", studioOnDragStart);
  tr.addEventListener("dragover", studioOnDragOver);
  tr.addEventListener("drop", studioOnDrop);
  tr.addEventListener("dragend", studioOnDragEnd);
  tr.innerHTML = `
    <td class="studio-td--center"><button class="studio-btn--move" title="Arrastar">&#x2630;</button><span class="studio-grid__order-num">${idx + 1}</span></td>
    <td><input type="text" value="NewField" data-field="key" /></td>
    <td><input type="text" value="Novo Campo" data-field="displayName" /></td>
    <td><input type="text" value="" data-field="aliases" /></td>
    <td class="studio-td--center"><input type="checkbox" data-field="isRequired" /></td>
    <td><select data-field="dataType">
      <option value="text" selected>text</option>
      <option value="number">number</option>
      <option value="date">date</option>
      <option value="currency">currency</option>
    </select></td>
    <td class="studio-td--actions">
      <button class="studio-btn--danger" title="Remover" onclick="studioRemoveRow(this)">&#x2716;</button>
    </td>`;
  tbody.appendChild(tr);
  tr.querySelector('input[data-field="key"]').focus();
  studioReindex();
}

function studioRemoveRow(btn) {
  const tr = btn.closest("tr");
  tr.remove();
  studioReindex();
}

function studioReindex() {
  const rows = document.querySelectorAll("#studioGridBody tr");
  rows.forEach((tr, i) => {
    tr.dataset.idx = i;
    const numSpan = tr.querySelector(".studio-grid__order-num");
    if (numSpan) numSpan.textContent = i + 1;
  });
}

function studioCollectRows() {
  const rows = [];
  document.querySelectorAll("#studioGridBody tr").forEach((tr, i) => {
    const get = (f) => {
      const el = tr.querySelector(`[data-field="${f}"]`);
      if (!el) return "";
      if (el.type === "checkbox") return el.checked;
      return el.value.trim();
    };
    const key = get("key");
    if (!key) return;
    rows.push({
      key,
      displayName: get("displayName"),
      aliases: get("aliases"),
      isRequired: get("isRequired"),
      dataType: get("dataType") || "text",
      sortOrder: i + 1
    });
  });
  return rows;
}

function studioOnDragStart(e) {
  studioDragSrcRow = this;
  this.classList.add("studio-row--dragging");
  e.dataTransfer.effectAllowed = "move";
}

function studioOnDragOver(e) {
  e.preventDefault();
  e.dataTransfer.dropEffect = "move";
  this.classList.add("studio-row--over");
}

function studioOnDrop(e) {
  e.preventDefault();
  if (studioDragSrcRow === this) return;
  const tbody = this.parentNode;
  const allRows = [...tbody.children];
  const fromIdx = allRows.indexOf(studioDragSrcRow);
  const toIdx = allRows.indexOf(this);
  if (fromIdx < toIdx) {
    tbody.insertBefore(studioDragSrcRow, this.nextSibling);
  } else {
    tbody.insertBefore(studioDragSrcRow, this);
  }
  studioReindex();
}

function studioOnDragEnd() {
  document.querySelectorAll("#studioGridBody tr").forEach(r => {
    r.classList.remove("studio-row--dragging", "studio-row--over");
  });
  studioDragSrcRow = null;
}

async function createTemplate() {
  const columns = studioCollectRows();
  if (!columns.length) return alert("Adicione pelo menos uma coluna na grid.");

  const payload = {
    name: document.getElementById("templateName").value || "Template Custom",
    isDefault: document.getElementById("templateIsDefault").checked,
    columns
  };

  const selectedId = document.getElementById("templateSelect").value;
  const isUpdate = !!selectedId;
  const endpoint = isUpdate
    ? `/api/shipper/templates/${selectedId}`
    : "/api/shipper/templates";
  const method = isUpdate ? "PUT" : "POST";

  const res = await fetch(apiUrl(endpoint), {
    method,
    headers: { ...authHeaders(), "Content-Type": "application/json" },
    body: JSON.stringify(payload)
  });
  if (!res.ok) return alert(await res.text());
  alert(isUpdate ? "Template atualizado." : "Template criado.");
  await loadTemplates();
}

async function loadTemplates() {
  const res = await fetch(apiUrl("/api/shipper/templates"), { headers: authHeaders() });
  if (!res.ok) return;
  const templates = await res.json();
  const select = document.getElementById("templateSelect");
  select.innerHTML = templates
    .map(t => `<option value="${t.id}">${t.name}${t.isDefault ? " (padrão)" : ""}</option>`)
    .join("");

  if (templates.length > 0) {
    select.value = templates[0].id;
    studioLoadFromTemplate(templates[0]);
  } else {
    studioRenderGrid(STUDIO_DEFAULT_ROWS);
  }

  select.onchange = () => {
    const tmpl = templates.find(t => t.id === select.value);
    if (tmpl) studioLoadFromTemplate(tmpl);
  };

  bidPopulateTemplateSelect();
}

function studioLoadFromTemplate(tmpl) {
  document.getElementById("templateName").value = tmpl.name || "";
  document.getElementById("templateIsDefault").checked = tmpl.isDefault || false;
  if (tmpl.columns && tmpl.columns.length) {
    const sorted = [...tmpl.columns].sort((a, b) => a.sortOrder - b.sortOrder);
    studioRenderGrid(sorted);
  } else {
    studioRenderGrid(STUDIO_DEFAULT_ROWS);
  }
}

async function downloadTemplateExcel() {
  const templateId = document.getElementById("templateSelect").value;
  if (!templateId) return alert("Selecione um template.");
  const res = await fetch(apiUrl(`/api/shipper/templates/${templateId}/export-excel`), { headers: authHeaders() });
  if (!res.ok) return alert("Falha ao exportar template.");
  const blob = await res.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = "bid-template.xlsx";
  a.click();
  URL.revokeObjectURL(url);
}

async function mapExcelTemplate() {
  const templateId = document.getElementById("templateSelect").value;
  const file = document.getElementById("templateMapFile").files[0];
  if (!templateId) return alert("Selecione um template.");
  if (!file) return alert("Selecione uma planilha.");

  const form = new FormData();
  form.append("excelFile", file);

  const res = await fetch(apiUrl(`/api/shipper/templates/${templateId}/map-excel`), {
    method: "POST",
    headers: authHeaders(),
    body: form
  });
  if (!res.ok) return alert(await res.text());
  const data = await res.json();

  const rows = data.matches
    .map(m => {
      const pct = (m.confidence * 100);
      const cls = pct >= 80 ? "confidence-high" : pct >= 50 ? "confidence-mid" : "confidence-low";
      return `<tr>
        <td>${m.displayName}</td>
        <td>${m.matchedHeader || "<em>-</em>"}</td>
        <td class="${cls}">${number(pct)}%</td>
        <td>${m.isRequired ? "<strong>Sim</strong>" : "Não"}</td>
      </tr>`;
    }).join("");
  const missing = (data.missingRequired || []).join(", ");
  document.getElementById("templateMappingResult").innerHTML = `
    <table class="table">
      <thead><tr><th>Campo Template</th><th>Coluna Encontrada</th><th>Confiança</th><th>Obrigatório</th></tr></thead>
      <tbody>${rows}</tbody>
    </table>
    ${missing ? `<div style="margin-top:8px;color:#c0392b"><strong>Obrigatórios não mapeados:</strong> ${missing}</div>` : '<div style="margin-top:8px;color:#1a8754"><strong>Todos os campos obrigatórios mapeados.</strong></div>'}
  `;
}

async function loadAdminShippers() {
  const res = await fetch(apiUrl("/api/admin/shippers"), { headers: authHeaders() });
  if (!res.ok) return;
  const shippers = await res.json();
  document.getElementById("adminShipperSelect").innerHTML = shippers
    .map(s => `<option value="${s.id}">${s.company} (${s.email})</option>`)
    .join("");
}

async function loadAdminProfiles() {
  const shipperId = document.getElementById("adminShipperSelect").value;
  if (!shipperId) return;
  const res = await fetch(apiUrl(`/api/admin/mapping-profiles?shipperId=${shipperId}`), { headers: authHeaders() });
  if (!res.ok) return;
  adminProfiles = await res.json();
  document.getElementById("adminProfileSelect").innerHTML = adminProfiles
    .map(p => `<option value="${p.id}">${p.name}${p.isActive ? " (ativo)" : ""}</option>`)
    .join("");
  renderAdminGrid();
}

function renderAdminGrid() {
  const profileId = document.getElementById("adminProfileSelect").value;
  const profile = adminProfiles.find(p => p.id === profileId) || adminProfiles[0];
  const rows = (profile?.rules || []).sort((a, b) => a.sortOrder - b.sortOrder);
  document.getElementById("adminGridBody").innerHTML = rows
    .map(r => `
      <tr>
        <td contenteditable="true">${r.canonicalField || ""}</td>
        <td contenteditable="true">${r.displayName || ""}</td>
        <td contenteditable="true">${r.aliases || ""}</td>
        <td contenteditable="true">${r.isRequired ? "true" : "false"}</td>
        <td contenteditable="true">${r.dataType || "text"}</td>
        <td contenteditable="true">${r.sortOrder ?? 0}</td>
      </tr>
    `).join("");
}

function addAdminGridRow() {
  const tbody = document.getElementById("adminGridBody");
  const tr = document.createElement("tr");
  tr.innerHTML = `
    <td contenteditable="true">CustomField</td>
    <td contenteditable="true">Campo Custom</td>
    <td contenteditable="true">campo custom,custom field</td>
    <td contenteditable="true">false</td>
    <td contenteditable="true">text</td>
    <td contenteditable="true">99</td>`;
  tbody.appendChild(tr);
}

function newAdminProfile() {
  document.getElementById("adminProfileSelect").value = "";
  document.getElementById("adminGridBody").innerHTML = "";
  addAdminGridRow();
}

function collectAdminGridRules() {
  const rows = [...document.querySelectorAll("#adminGridBody tr")];
  return rows.map(row => {
    const cells = [...row.querySelectorAll("td")].map(td => td.textContent.trim());
    return {
      canonicalField: cells[0] || "",
      displayName: cells[1] || "",
      aliases: cells[2] || "",
      isRequired: (cells[3] || "").toLowerCase() === "true",
      dataType: cells[4] || "text",
      sortOrder: Number(cells[5] || 0)
    };
  }).filter(r => r.canonicalField);
}

async function saveAdminProfile() {
  const shipperId = document.getElementById("adminShipperSelect").value;
  if (!shipperId) return alert("Selecione o embarcador.");
  const profileId = document.getElementById("adminProfileSelect").value;
  const rules = collectAdminGridRules();
  if (!rules.length) return alert("Adicione regras na grid.");

  const name = profileId
    ? (adminProfiles.find(p => p.id === profileId)?.name || "Perfil Custom")
    : prompt("Nome do novo perfil de mapeamento:", "Perfil Cliente");
  if (!name) return;

  const payload = { shipperId, name, isActive: true, rules };
  const endpoint = profileId ? `/api/admin/mapping-profiles/${profileId}` : "/api/admin/mapping-profiles";
  const method = profileId ? "PUT" : "POST";
  const res = await fetch(apiUrl(endpoint), {
    method,
    headers: { ...authHeaders(), "Content-Type": "application/json" },
    body: JSON.stringify(payload)
  });
  if (!res.ok) return alert(await res.text());
  alert("Perfil salvo.");
  await loadAdminProfiles();
}

async function analyzeAdminProfile() {
  const profileId = document.getElementById("adminProfileSelect").value;
  const file = document.getElementById("adminAnalyzeFile").files[0];
  if (!profileId) return alert("Selecione um perfil.");
  if (!file) return alert("Selecione uma planilha.");

  const form = new FormData();
  form.append("excelFile", file);
  const res = await fetch(apiUrl(`/api/admin/mapping-profiles/${profileId}/analyze-excel`), {
    method: "POST",
    headers: authHeaders(),
    body: form
  });
  if (!res.ok) return alert(await res.text());
  const data = await res.json();
  const rows = data.matches
    .map(m => `<tr><td>${m.displayName}</td><td>${m.matchedHeader || "-"}</td><td>${number(m.confidence * 100)}%</td><td>${m.isRequired ? "Sim" : "Não"}</td></tr>`)
    .join("");
  document.getElementById("adminAnalyzeResult").innerHTML = `
    <table class="table">
      <tr><th>Campo</th><th>Header Encontrado</th><th>Confiança</th><th>Obrigatório</th></tr>
      ${rows}
    </table>`;
}

/* ---------- BID CREATION ---------- */

let bidTemplateColumns = [];

function bidPopulateTemplateSelect() {
  const main = document.getElementById("templateSelect");
  const bid = document.getElementById("bidTemplateSelect");
  if (!main || !bid) return;
  bid.innerHTML = main.innerHTML;
  bid.value = main.value;
  bidLoadTemplateColumns();
}

function bidPopulateOriginSelect() {
  const sel = document.getElementById("bidOriginFacility");
  if (!sel) return;
  const current = sel.value || "";
  const options = bidOriginFacilitiesList()
    .map(f => `<option value="${f.id}">${escapeHtml(`${f.name} (${f.city}/${f.state})`)}</option>`)
    .join("");
  sel.innerHTML = `<option value="">-- selecione --</option>${options}`;
  if (current && sel.querySelector(`option[value="${current}"]`)) {
    sel.value = current;
  }
  bidSyncLaneOriginSelects();
}

function bidLoadTemplateColumns() {
  const templateId = document.getElementById("bidTemplateSelect").value;
  if (!templateId) {
    bidTemplateColumns = [];
    bidRenderLanesHeader();
    return;
  }
  const sel = document.getElementById("templateSelect");
  const opt = sel?.querySelector(`option[value="${templateId}"]`);
  if (!opt) return;

  const res = fetch(apiUrl(`/api/shipper/templates`), { headers: authHeaders() })
    .then(r => r.json())
    .then(templates => {
      const tmpl = templates.find(t => t.id === templateId);
      if (tmpl && tmpl.columns) {
        bidTemplateColumns = tmpl.columns.sort((a, b) => a.sortOrder - b.sortOrder);
      } else {
        bidTemplateColumns = [];
      }
      bidRenderLanesHeader();
      const tbody = document.getElementById("bidLanesBody");
      if (!tbody.children.length) bidAddLaneRow();
    });
}

function bidRenderLanesHeader() {
  const thead = document.getElementById("bidLanesHead");
  if (!thead) return;
  if (!bidTemplateColumns.length) {
    thead.innerHTML = `<tr>
      <th class="studio-grid__th">Origem</th>
      <th class="studio-grid__th">Destino</th>
      <th class="studio-grid__th">Tipo Frete</th>
      <th class="studio-grid__th">Volume</th>
      <th class="studio-grid__th">Ve\u00edculo</th>
      <th class="studio-grid__th">SLA</th>
      <th class="studio-grid__th">Regi\u00e3o</th>
      <th class="studio-grid__th studio-grid__th--center" style="width:50px"></th>
    </tr>`;
    return;
  }
  const ths = bidTemplateColumns.map(c =>
    `<th class="studio-grid__th">${c.displayName}${c.isRequired ? ' <span style="color:#c0392b">*</span>' : ""}</th>`
  ).join("");
  thead.innerHTML = `<tr>${ths}<th class="studio-grid__th studio-grid__th--center" style="width:50px"></th></tr>`;
}

function bidAddLaneRow() {
  const tbody = document.getElementById("bidLanesBody");
  if (!tbody) return;
  const tr = document.createElement("tr");
  const mainOriginId = document.getElementById("bidOriginFacility")?.value || "";

  if (!bidTemplateColumns.length) {
    tr.innerHTML = `
      <td>${bidOriginFacilitySelectHtml(mainOriginId)}</td>
      <td>${bidDestinationDeliveryPointSelectHtml("")}</td>
      <td><input type="text" data-key="FreightType" placeholder="CIF" style="width:100%;padding:5px 8px;border:1px solid #d6e0f0;border-radius:3px;font-size:13px" /></td>
      <td><input type="number" data-key="VolumeForecast" placeholder="0" step="0.01" style="width:80px;padding:5px 8px;border:1px solid #d6e0f0;border-radius:3px;font-size:13px" /></td>
      <td><input type="text" data-key="VehicleType" placeholder="Carreta" style="width:100%;padding:5px 8px;border:1px solid #d6e0f0;border-radius:3px;font-size:13px" /></td>
      <td><input type="text" data-key="SlaRequirements" placeholder="48h" style="width:100%;padding:5px 8px;border:1px solid #d6e0f0;border-radius:3px;font-size:13px" /></td>
      <td><input type="text" data-key="Region" placeholder="Sul" style="width:100%;padding:5px 8px;border:1px solid #d6e0f0;border-radius:3px;font-size:13px" /></td>
      <td class="studio-td--center"><button class="studio-btn--danger" onclick="this.closest('tr').remove()" title="Remover">&#x2716;</button></td>`;
  } else {
    const cells = bidTemplateColumns
      .map(c => {
        if ((c.key || "").toLowerCase() === "origin") {
          return `<td>${bidOriginFacilitySelectHtml(mainOriginId)}</td>`;
        }
        if ((c.key || "").toLowerCase() === "destination") {
          return `<td>${bidDestinationDeliveryPointSelectHtml("")}</td>`;
        }
        const inputType = c.dataType === "number" || c.dataType === "currency" ? "number" : "text";
        const step = inputType === "number" ? ' step="0.01"' : "";
        return `<td><input type="${inputType}" data-key="${c.key}"${step} placeholder="${escapeHtml(c.displayName)}" style="width:100%;padding:5px 8px;border:1px solid #d6e0f0;border-radius:3px;font-size:13px" /></td>`;
      })
      .join("");
    tr.innerHTML = cells + `<td class="studio-td--center"><button class="studio-btn--danger" onclick="this.closest('tr').remove()" title="Remover">&#x2716;</button></td>`;
  }

  tbody.appendChild(tr);
  tr.querySelector("select, input")?.focus();
}

function bidClearLanes() {
  document.getElementById("bidLanesBody").innerHTML = "";
  bidAddLaneRow();
}

function bidCollectLanes() {
  const rows = [...document.querySelectorAll("#bidLanesBody tr")];
  return rows.map(tr => {
    const fields = tr.querySelectorAll("[data-key]");
    const lane = { destinationDeliveryPointId: null };
    fields.forEach(el => {
      const k = el.dataset.key;
      if (!k) return;
      if (el.tagName === "SELECT") {
        const id = el.value;
        if (!id) {
          lane[k] = "";
        } else if (k === "Origin") {
          const fac = facilitiesCache.find(x => String(x.id) === String(id));
          lane[k] = fac ? `${fac.name} (${fac.city}/${fac.state})` : "";
        } else if (k === "Destination") {
          const pt = deliveryPointsCache.find(x => String(x.id) === String(id));
          lane[k] = pt ? `${pt.name} (${pt.city}/${pt.state})` : "";
          lane.destinationDeliveryPointId = id;
        } else {
          lane[k] = el.value;
        }
      } else {
        lane[k] = el.type === "number" ? (parseFloat(el.value) || 0) : el.value.trim();
      }
    });
    return {
      origin: lane.Origin || "",
      destination: lane.Destination || "",
      destinationDeliveryPointId: lane.destinationDeliveryPointId,
      freightType: lane.FreightType || "",
      volumeForecast: parseFloat(lane.VolumeForecast) || 0,
      slaRequirements: lane.SlaRequirements || "",
      vehicleType: lane.VehicleType || "",
      insuranceRequirements: lane.InsuranceRequirements || "",
      paymentTerms: lane.PaymentTerms || "",
      region: lane.Region || ""
    };
  }).filter(l => l.origin && l.destinationDeliveryPointId);
}

async function bidCreate() {
  const lanes = bidCollectLanes();
  if (!lanes.length) {
    return alert("Adicione ao menos uma rota com origem (CD) e destino (ponto de entrega cadastrado) em cada linha.");
  }

  const deadlineVal = document.getElementById("deadline").value;
  const payload = {
    title: document.getElementById("bidTitle").value,
    auctionType: document.getElementById("auctionType").value,
    deadlineUtc: deadlineVal ? new Date(deadlineVal).toISOString() : null,
    requiredDocumentation: document.getElementById("requiredDocs").value,
    baselineContractValue: parseFloat(document.getElementById("baselineValue").value) || 0,
    originFacilityId: document.getElementById("bidOriginFacility").value || null,
    templateId: document.getElementById("bidTemplateSelect").value || null,
    lanes
  };

  const res = await fetch(apiUrl("/api/shipper/bids"), {
    method: "POST",
    headers: { ...authHeaders(), "Content-Type": "application/json" },
    body: JSON.stringify(payload)
  });

  if (!res.ok) return alert(await res.text());
  const data = await res.json();
  alert(`BID "${data.title}" criado com ${data.laneCount} rotas!`);
  bidClearLanes();
  await loadBids();
}

async function bidGenerateRoutes() {
  const originId = document.getElementById("bidOriginFacility").value;
  if (!originId) return alert("Selecione o CD de origem antes de gerar as rotas automaticamente.");

  const btn    = document.getElementById("btnGenerateRoutes");
  const status = document.getElementById("routeEngineStatus");
  btn.disabled = true;
  status.style.color = "#8a9ab8";
  status.textContent = "Calculando rotas...";

  try {
    const res = await fetch(apiUrl(`/api/shipper/route-engine/suggestions?originFacilityId=${originId}`), {
      headers: authHeaders()
    });

    if (!res.ok) {
      const err = await res.text();
      status.style.color = "#c0392b";
      status.textContent = "Erro: " + err;
      return;
    }

    const suggestions = await res.json();
    if (!suggestions.length) {
      status.style.color = "#e67e22";
      status.textContent = "Nenhuma rota calculada. Verifique CEP/coordenadas dos pontos de entrega e se a API key ORS está configurada.";
      return;
    }

    // Reset grid to default (no template columns)
    bidTemplateColumns = [];
    bidRenderLanesHeader();
    document.getElementById("bidLanesBody").innerHTML = "";

    suggestions.forEach(s => {
      const tbody = document.getElementById("bidLanesBody");
      const tr = document.createElement("tr");
      tr.dataset.originFacilityId = s.originFacilityId;
      tr.dataset.destDeliveryPointId = s.destDeliveryPointId;

      const durText = s.durationHours < 1
        ? `${Math.round(s.durationHours * 60)}min`
        : `${s.durationHours.toFixed(1)}h`;

      const costFmt = s.estimatedCost > 0
        ? `R$ ${s.estimatedCost.toLocaleString("pt-BR", { minimumFractionDigits: 2 })}`
        : "";

      const title = `#${s.rank} | Distância: ${s.distanceKm} km | Tempo: ${durText} | ${costFmt}`;

      tr.innerHTML = `
        <td title="${title}">
          ${bidOriginFacilitySelectHtml(s.originFacilityId)}
        </td>
        <td title="${title}">
          ${bidDestinationDeliveryPointSelectHtml(s.destDeliveryPointId)}
        </td>
        <td><input type="text" data-key="FreightType" placeholder="CIF" style="width:100%;padding:5px 8px;border:1px solid #d6e0f0;border-radius:3px;font-size:13px" /></td>
        <td><input type="number" data-key="VolumeForecast" value="0" step="0.01" style="width:80px;padding:5px 8px;border:1px solid #d6e0f0;border-radius:3px;font-size:13px" /></td>
        <td><input type="text" data-key="VehicleType" value="${escapeHtml(s.suggestedVehicleType)}" style="width:100%;padding:5px 8px;border:1px solid #d6e0f0;border-radius:3px;font-size:13px" /></td>
        <td><input type="text" data-key="SlaRequirements" placeholder="48h" style="width:100%;padding:5px 8px;border:1px solid #d6e0f0;border-radius:3px;font-size:13px" /></td>
        <td><input type="text" data-key="Region" value="${escapeHtml(s.destRegion || "")}" style="width:100%;padding:5px 8px;border:1px solid #d6e0f0;border-radius:3px;font-size:13px" /></td>
        <td class="studio-td--center" style="white-space:nowrap">
          <span title="${title}" style="font-size:11px;color:#0f6ebd;font-weight:600;margin-right:4px">${durText}</span>
          <span title="${title}" style="font-size:10px;color:#6c7a8f">${s.distanceKm}km</span>
          <button class="studio-btn--danger" onclick="this.closest('tr').remove()" title="Remover" style="margin-left:4px">&#x2716;</button>
        </td>`;

      tbody.appendChild(tr);
    });

    status.style.color = "#27ae60";
    status.textContent = `${suggestions.length} rota(s) gerada(s) e ordenada(s) por menor tempo.`;
  } finally {
    btn.disabled = false;
  }
}

async function loadBids() {
  const res = await fetch(apiUrl("/api/shipper/bids"), { headers: authHeaders() });
  const bids = await res.json();
  const tableRows = bids.map(b => `<tr>
    <td><strong>${b.title}</strong></td>
    <td>${b.auctionType}</td>
    <td>${new Date(b.deadlineUtc).toLocaleString("pt-BR")}</td>
    <td class="studio-td--center">${b.lanes}</td>
    <td class="studio-td--center">${b.invitedCarriers}</td>
    <td><span class="facility-badge ${b.status === "Open" ? "facility-badge--matriz" : "facility-badge--filial"}">${b.status}</span></td>
  </tr>`).join("");
  document.getElementById("bidList").innerHTML = tableRows
    ? `<table class="studio-grid mobile-card-table"><thead><tr>
        <th class="studio-grid__th">T\u00edtulo</th>
        <th class="studio-grid__th">Leil\u00e3o</th>
        <th class="studio-grid__th">Deadline</th>
        <th class="studio-grid__th studio-grid__th--center">Rotas</th>
        <th class="studio-grid__th studio-grid__th--center">Convidados</th>
        <th class="studio-grid__th">Status</th>
      </tr></thead><tbody>${tableRows}</tbody></table>`
    : '<p style="color:#8a9ab8;text-align:center;padding:20px">Nenhum BID criado ainda.</p>';
  applyMobileTableLabels("#bidList .mobile-card-table");

  const options = bids.map(b => `<option value="${b.id}" data-deadline="${b.deadlineUtc}">${b.title}</option>`).join("");
  document.getElementById("bidToInvite").innerHTML = options;
  document.getElementById("dashboardBid").innerHTML = options;
}

async function loadCarriers() {
  const res = await fetch(apiUrl("/api/system/carriers"), { headers: authHeaders() });
  const carriers = await res.json();
  document.getElementById("carrierChecklist").innerHTML = carriers
    .map(c => `<label><input type="checkbox" class="carrier-check" value="${c.id}"/> ${c.company} (${c.email})</label><br/>`)
    .join("");
}

async function inviteCarriers() {
  const bidId = document.getElementById("bidToInvite").value;
  const carrierIds = [...document.querySelectorAll(".carrier-check:checked")].map(i => i.value);
  const res = await fetch(apiUrl(`/api/shipper/bids/${bidId}/invite`), {
    method: "POST",
    headers: { ...authHeaders(), "Content-Type": "application/json" },
    body: JSON.stringify({ carrierIds })
  });
  if (!res.ok) return alert(await res.text());
  alert("Invitations sent.");
  await loadBids();
}

async function loadDashboard() {
  const bidId = document.getElementById("dashboardBid").value;
  if (!bidId) return;
  const region = document.getElementById("filterRegion").value;

  const bidOption = document.querySelector(`#dashboardBid option[value="${bidId}"]`);
  selectedBidDeadline = bidOption?.dataset.deadline ? new Date(bidOption.dataset.deadline) : null;

  const query = region ? `?region=${encodeURIComponent(region)}` : "";
  const res = await fetch(apiUrl(`/api/shipper/bids/${bidId}/dashboard${query}`), { headers: authHeaders() });
  if (!res.ok) return alert(await res.text());
  const data = await res.json();

  document.getElementById("kpiSavings").textContent = money(data.kpis.totalProjectedSavings);
  document.getElementById("kpiCost").textContent = money(data.kpis.costVsPreviousContract);
  document.getElementById("kpiPerf").textContent = number(data.kpis.carrierPerformanceIndex);

  renderCharts(data);
}

function renderCharts(data) {
  const labels = data.ranking.map(r => r.carrierName);
  const prices = data.ranking.map(r => r.totalPrice);
  const scores = data.ranking.map(r => r.score);
  const deviations = data.ranking.map(r => r.priceDeviation);

  if (priceChart) priceChart.destroy();
  if (deviationChart) deviationChart.destroy();
  if (heatmapChart) heatmapChart.destroy();

  priceChart = new Chart(document.getElementById("priceChart"), {
    type: "bar",
    data: { labels, datasets: [{ label: "Total Price", data: prices }, { label: "Score", data: scores }] }
  });

  deviationChart = new Chart(document.getElementById("deviationChart"), {
    type: "line",
    data: { labels, datasets: [{ label: "Price Deviation %", data: deviations }] }
  });

  heatmapChart = new Chart(document.getElementById("heatmapChart"), {
    type: "bar",
    data: {
      labels: data.heatmap.map(h => h.region),
      datasets: [{ label: "Average Volume", data: data.heatmap.map(h => h.avgVolume) }]
    }
  });
}

async function exportDashboard(format) {
  const bidId = document.getElementById("dashboardBid").value;
  if (!bidId) return;
  const res = await fetch(apiUrl(`/api/shipper/bids/${bidId}/export/${format}`), { headers: authHeaders() });
  if (!res.ok) return alert("Export failed.");
  const blob = await res.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = `dashboard.${format === "excel" ? "xlsx" : "pdf"}`;
  a.click();
  URL.revokeObjectURL(url);
}

async function loadAudit() {
  const res = await fetch(apiUrl("/api/system/audit-log"), { headers: authHeaders() });
  if (!res.ok) return;
  const logs = await res.json();
  document.getElementById("auditLog").innerHTML = logs.map(x => `<div><span class="badge">${x.action}</span> ${x.entityType} ${x.entityId} - ${x.details}</div>`).join("");
}

async function loadInvitedBids() {
  const res = await fetch(apiUrl("/api/carrier/invited-bids"), { headers: authHeaders() });
  const bids = await res.json();
  document.getElementById("invitedBids").innerHTML = bids.map(b => `<div><strong>${b.title}</strong> | Deadline: ${new Date(b.deadlineUtc).toLocaleString()} | Lanes: ${b.laneCount}</div>`).join("");
  document.getElementById("carrierBidSelect").innerHTML = bids.map(b => `<option value="${b.bidEventId}">${b.title}</option>`).join("");
  await loadLanePricingForm();
}

async function loadLanePricingForm() {
  const bidId = document.getElementById("carrierBidSelect").value;
  if (!bidId) return;
  const res = await fetch(apiUrl(`/api/carrier/bids/${bidId}`), { headers: authHeaders() });
  const lanes = await res.json();
  document.getElementById("lanePricing").innerHTML = lanes.map(l => `
    <div class="grid3 carrier-lane-card">
      <span>${l.origin} -> ${l.destination} (${l.region || "N/A"})</span>
      <span>${l.vehicleType} | Vol ${l.volumeForecast}</span>
      <input type="number" step="0.01" class="lane-price" data-lane="${l.id}" placeholder="Price per lane" />
    </div>
  `).join("");
  await loadProposalVersions();
}

async function saveProposalDraft() {
  await saveProposal("draft");
}

async function submitProposal() {
  await saveProposal("submit");
}

async function saveProposal(mode) {
  const bidId = document.getElementById("carrierBidSelect").value;
  const lanePrices = [...document.querySelectorAll(".lane-price")]
    .filter(x => x.value)
    .map(x => ({ laneId: x.dataset.lane, pricePerLane: Number(x.value) }));

  const payload = {
    bidId,
    lanePrices,
    operationalCapacityTons: Number(document.getElementById("capacity").value || 0),
    slaCompliant: document.getElementById("slaCompliant").checked
  };

  const endpoint = mode === "draft" ? "/api/carrier/proposals/save-draft" : "/api/carrier/proposals/submit";
  const res = await fetch(apiUrl(endpoint), {
    method: "POST",
    headers: { ...authHeaders(), "Content-Type": "application/json" },
    body: JSON.stringify(payload)
  });
  if (!res.ok) return alert(await res.text());
  const data = await res.json();
  alert(`Proposal ${data.status} (v${data.version}) total ${money(data.totalPrice)}`);
  await loadProposalVersions();
}

async function loadProposalVersions() {
  const bidId = document.getElementById("carrierBidSelect").value;
  if (!bidId) return;
  const res = await fetch(apiUrl(`/api/carrier/bids/${bidId}/proposal-versions`), { headers: authHeaders() });
  if (!res.ok) return;
  const versions = await res.json();
  document.getElementById("proposalVersions").innerHTML = versions.map(v => `<div><span class="badge">v${v.version}</span> ${v.status} | ${money(v.totalPrice)} | SLA ${v.slaCompliant ? "OK" : "No"} | ${new Date(v.savedAtUtc).toLocaleString()}</div>`).join("");
}

async function loadNotifications() {
  const res = await fetch(apiUrl("/api/system/notifications"), { headers: authHeaders() });
  if (!res.ok) return;
  const notes = await res.json();
  document.getElementById("notifications").innerHTML = notes.map(n => `<div><strong>${n.title}</strong> - ${n.message}</div>`).join("");
}

function money(value) {
  return new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value || 0);
}

function number(value) {
  return Number(value || 0).toFixed(2);
}

function initDragDrop() {
  document.getElementById("carrierBidSelect").addEventListener("change", loadLanePricingForm);
  const adminShipper = document.getElementById("adminShipperSelect");
  const adminProfile = document.getElementById("adminProfileSelect");
  const bidOrigin = document.getElementById("bidOriginFacility");
  if (adminShipper) adminShipper.addEventListener("change", loadAdminProfiles);
  if (adminProfile) adminProfile.addEventListener("change", renderAdminGrid);
  if (bidOrigin) {
    bidOrigin.addEventListener("change", () => {
      if (bidOrigin.value) bidSyncLaneOriginSelects(bidOrigin.value);
    });
  }

  const bidLanesBody = document.getElementById("bidLanesBody");
  if (bidLanesBody && !bidLanesBody.dataset.destDelegation) {
    bidLanesBody.dataset.destDelegation = "1";
    bidLanesBody.addEventListener("change", bidLanesBodyOnChangeDest);
  }

  const backdrop = document.getElementById("sidebarBackdrop");
  if (backdrop) {
    backdrop.addEventListener("click", closeMobileSidebar);
  }

  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape" && isMobileViewport()) {
      closeMobileSidebar();
    }
  });

  window.addEventListener("resize", syncSidebarByViewport);
  syncSidebarByViewport();
}

/* ---------- LOG VIEWER ---------- */

const LOG_LEVEL_COLORS = {
  Info:  { bg: "#e6f9e6", fg: "#1a7a1a", badge: "#28a745" },
  Warn:  { bg: "#fff9e0", fg: "#8a6d00", badge: "#ffc107" },
  Error: { bg: "#fde8e8", fg: "#b71c1c", badge: "#dc3545" },
  Debug: { bg: "#e3ecfa", fg: "#1a4a8a", badge: "#007bff" }
};

async function loadLogServices() {
  const res = await fetch(apiUrl("/api/log/services"), { headers: authHeaders() });
  if (!res.ok) return;
  const services = await res.json();
  const sel = document.getElementById("logFilterService");
  sel.innerHTML = `<option value="">Todos</option>` + services.map(s => `<option value="${s}">${s}</option>`).join("");
}

async function searchLogs(page) {
  logCurrentPage = page || 1;
  const from = document.getElementById("logFilterFrom").value;
  const to = document.getElementById("logFilterTo").value;
  const level = document.getElementById("logFilterLevel").value;
  const service = document.getElementById("logFilterService").value;
  const text = document.getElementById("logFilterText").value;
  const correlationId = document.getElementById("logFilterCorrelation").value;
  const userEmail = document.getElementById("logFilterUser").value;

  const params = new URLSearchParams();
  if (from) params.set("from", new Date(from).toISOString());
  if (to) params.set("to", new Date(to).toISOString());
  if (level) params.set("level", level);
  if (service) params.set("service", service);
  if (text) params.set("text", text);
  if (correlationId) params.set("correlationId", correlationId);
  if (userEmail) params.set("userEmail", userEmail);
  params.set("page", logCurrentPage);
  params.set("pageSize", 50);

  const res = await fetch(apiUrl(`/api/log?${params.toString()}`), { headers: authHeaders() });
  if (!res.ok) return alert("Falha ao buscar logs.");
  const data = await res.json();

  renderLogTable(data);
  renderLogStats();
}

function renderLogTable(data) {
  const rows = data.items.map(l => {
    const c = LOG_LEVEL_COLORS[l.level] || LOG_LEVEL_COLORS.Info;
    const ts = new Date(l.timestamp);
    const time = ts.toLocaleTimeString("pt-BR", { hour12: false, hour: "2-digit", minute: "2-digit", second: "2-digit" });
    const date = ts.toLocaleDateString("pt-BR");
    const shortMsg = l.message.length > 90 ? l.message.slice(0, 90) + "..." : l.message;
    return `
      <tr class="log-row" style="background:${c.bg}; cursor:pointer;" onclick="toggleLogDetail(this)">
        <td style="white-space:nowrap">${date} ${time}</td>
        <td><span class="log-badge" style="background:${c.badge};color:#fff">${l.level}</span></td>
        <td>${l.service}</td>
        <td class="log-corr" title="${l.correlationId}">${l.correlationId || "-"}</td>
        <td title="${l.message}">${shortMsg}</td>
        <td>${l.httpMethod || ""} ${l.httpStatus || ""}</td>
        <td>${l.elapsedMs != null ? l.elapsedMs + "ms" : ""}</td>
        <td>${l.userEmail || "-"}</td>
      </tr>
      <tr class="log-detail hidden">
        <td colspan="8">
          <div class="log-detail-box">
            <strong>Correlation ID:</strong> ${l.correlationId}<br/>
            <strong>Request:</strong> ${l.httpMethod || ""} ${l.requestPath || ""}<br/>
            <strong>IP:</strong> ${l.ipAddress || "-"}<br/>
            <strong>User:</strong> ${l.userEmail || "-"} (${l.userId || "-"})<br/>
            <strong>Full Message:</strong> ${l.message}<br/>
            ${l.stackTrace ? `<strong>Stack Trace:</strong><pre class="log-stack">${l.stackTrace}</pre>` : ""}
          </div>
        </td>
      </tr>
    `;
  }).join("");

  const pagination = renderLogPagination(data);

  document.getElementById("logTableContainer").innerHTML = `
    <div class="log-summary">
      Exibindo <strong>${data.items.length}</strong> de <strong>${data.totalCount}</strong> registros | Página ${data.page} de ${data.totalPages}
    </div>
    <table class="table log-table">
      <thead>
        <tr>
          <th>Timestamp</th>
          <th>Level</th>
          <th>Serviço</th>
          <th>CorrelationId</th>
          <th>Mensagem</th>
          <th>HTTP</th>
          <th>Tempo</th>
          <th>Usuário</th>
        </tr>
      </thead>
      <tbody>${rows}</tbody>
    </table>
    ${pagination}
  `;
  applyMobileTableLabels(".log-table");
}

function renderLogPagination(data) {
  if (data.totalPages <= 1) return "";
  let btns = "";
  const start = Math.max(1, data.page - 3);
  const end = Math.min(data.totalPages, data.page + 3);
  if (data.page > 1) btns += `<button class="log-page-btn" onclick="searchLogs(${data.page - 1})">&laquo;</button>`;
  for (let p = start; p <= end; p++) {
    btns += `<button class="log-page-btn${p === data.page ? " active" : ""}" onclick="searchLogs(${p})">${p}</button>`;
  }
  if (data.page < data.totalPages) btns += `<button class="log-page-btn" onclick="searchLogs(${data.page + 1})">&raquo;</button>`;
  return `<div class="log-pagination">${btns}</div>`;
}

function toggleLogDetail(row) {
  const detail = row.nextElementSibling;
  if (detail) detail.classList.toggle("hidden");
}

async function renderLogStats() {
  const from = document.getElementById("logFilterFrom").value;
  const to = document.getElementById("logFilterTo").value;
  const params = new URLSearchParams();
  if (from) params.set("from", new Date(from).toISOString());
  if (to) params.set("to", new Date(to).toISOString());

  const res = await fetch(apiUrl(`/api/log/stats?${params.toString()}`), { headers: authHeaders() });
  if (!res.ok) return;
  const stats = await res.json();

  const container = document.getElementById("logStatsCards");
  container.innerHTML = ["Info", "Warn", "Error", "Debug"].map(level => {
    const c = LOG_LEVEL_COLORS[level];
    const count = (stats.find(s => s.level === level) || {}).count || 0;
    return `<div class="log-stat-card" style="border-left: 4px solid ${c.badge}; background:${c.bg}">
      <span class="log-stat-level" style="color:${c.fg}">${level}</span>
      <span class="log-stat-count" style="color:${c.fg}">${count}</span>
    </div>`;
  }).join("");
}

function clearLogFilters() {
  document.getElementById("logFilterFrom").value = "";
  document.getElementById("logFilterTo").value = "";
  document.getElementById("logFilterLevel").value = "";
  document.getElementById("logFilterService").value = "";
  document.getElementById("logFilterText").value = "";
  document.getElementById("logFilterCorrelation").value = "";
  document.getElementById("logFilterUser").value = "";
  searchLogs(1);
}

/* ---------- INIT ---------- */

setDefaultDeadline();
initDragDrop();
studioRenderGrid(STUDIO_DEFAULT_ROWS);
setInterval(() => {
  if (!selectedBidDeadline) return;
  const diff = selectedBidDeadline - new Date();
  if (diff <= 0) return (document.getElementById("countdown").textContent = "Closed");
  const h = Math.floor(diff / 3600000);
  const m = Math.floor((diff % 3600000) / 60000);
  document.getElementById("countdown").textContent = `${h}h ${m}m`;
}, 1000);
