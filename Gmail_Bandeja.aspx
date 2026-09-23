<%@ Page Title="Bandeja Gmail" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="Gmail_Bandeja.aspx.cs" Inherits="RecepcionDocumental.Gmail_Bandeja" %>
<%@ Register Src="~/Controls/WsListadoAgrupado.ascx" TagPrefix="ws" TagName="ListadoAgrupado" %>
<asp:Content ID="BodyContent" ContentPlaceHolderID="MainContent" runat="server">
<style type="text/css">
    .sync-overlay { display: none; position: fixed; inset: 0; z-index: 2147483647; background: rgba(16, 24, 40, .78); align-items: center; justify-content: center; padding: 1.5rem; }
    .sync-overlay.is-active { display: flex; }
    .sync-overlay-card { width: min(34rem, 100%); border-radius: .75rem; background: #fff; padding: 2rem; text-align: center; box-shadow: 0 1.5rem 4rem rgba(0, 0, 0, .35); }
    .sync-spinner { width: 3rem; height: 3rem; margin: 0 auto 1.25rem; border: .35rem solid #dbe4f0; border-top-color: #0d6efd; border-radius: 50%; animation: sync-spin .8s linear infinite; }
    @keyframes sync-spin { to { transform: rotate(360deg); } }
</style>
<asp:Panel ID="pnlSyncOverlay" runat="server" ClientIDMode="Static" CssClass="sync-overlay" role="dialog" aria-modal="true" aria-labelledby="syncOverlayTitle" tabindex="-1">
    <div class="sync-overlay-card">
        <div class="sync-spinner" aria-hidden="true"></div>
        <h2 id="syncOverlayTitle" class="h4">Buscando nuevos correos...</h2>
        <p class="mb-2">El procesamiento puede tardar algunos minutos.</p>
        <p id="syncOverlayStatus" class="text-secondary mb-0" aria-live="polite">Esperando confirmación del estado...</p>
    </div>
</asp:Panel>
<main class="gmail-inbox-page">
    <header class="page-header d-flex justify-content-between align-items-end flex-wrap gap-3">
        <div>
            <p class="eyebrow">Gmail</p>
            <h1>Bandeja de entrada</h1>
        </div>
        <div class="d-flex gap-2">
            <asp:Button ID="btnBuscar" runat="server" ClientIDMode="Static" Text="Buscar nuevos correos" CssClass="btn btn-primary" UseSubmitBehavior="false" OnClientClick="beginGmailSync();" OnClick="Buscar_Click" />
            <a href="Gmail_Config.aspx" class="btn btn-outline-primary">Configurar cuenta</a>
        </div>
    </header>

    <asp:Panel ID="pnlDatabaseWarning" runat="server" ClientIDMode="Static" Visible="false" CssClass="alert alert-warning">
        <asp:Literal ID="litDatabaseWarning" runat="server" />
    </asp:Panel>

    <asp:HiddenField ID="hidSyncPolling" runat="server" ClientIDMode="Static" />
    <asp:HiddenField ID="hidSyncBaseline" runat="server" ClientIDMode="Static" />
    <p id="syncStatusText" class="text-secondary mb-3" aria-live="polite"><asp:Literal ID="litSyncStatus" runat="server" /></p>

    <asp:Panel ID="pnlSinCuenta" runat="server" Visible="false" CssClass="alert alert-info">
        No hay una cuenta Gmail activa. <a href="Gmail_Config.aspx">Configurar cuenta</a>.
    </asp:Panel>

    <asp:Panel ID="pnlResultado" runat="server" Visible="false" CssClass="alert alert-success">
        <h2 class="h5"><asp:Literal ID="litResultadoTitulo" runat="server" /></h2>
        <ul class="mb-0">
            <li>Mensajes encontrados: <asp:Literal ID="litEncontrados" runat="server" /></li>
            <li>Mensajes nuevos: <asp:Literal ID="litNuevos" runat="server" /></li>
            <li>Adjuntos analizados: <asp:Literal ID="litAnalizados" runat="server" /></li>
            <li>Facturas: <asp:Literal ID="litFacturas" runat="server" /></li>
            <li>Para revisar: <asp:Literal ID="litRevisar" runat="server" /></li>
            <li>Descartados: <asp:Literal ID="litDescartados" runat="server" /></li>
            <li>Documentos existentes: <asp:Literal ID="litDocumentosExistentes" runat="server" /></li>
            <li>Errores: <asp:Literal ID="litErrores" runat="server" /></li>
        </ul>
        <asp:Literal ID="litFallback" runat="server" />
    </asp:Panel>

    <ws:ListadoAgrupado ID="lstMensajes" runat="server" />
</main>
<script type="text/javascript">
    function setGmailSyncOverlay(active, text) {
        var overlay = document.getElementById('pnlSyncOverlay');
        if (!overlay) return;
        if (active) {
            overlay.classList.add('is-active');
            document.body.style.overflow = 'hidden';
            var status = document.getElementById('syncOverlayStatus');
            if (status && text) status.textContent = text;
            window.setTimeout(function () { overlay.focus(); }, 0);
        } else {
            overlay.classList.remove('is-active');
            document.body.style.overflow = '';
        }
    }

    function beginGmailSync() {
        var button = document.getElementById('btnBuscar');
        if (button) { button.disabled = true; button.value = 'Buscando…'; }
        setGmailSyncOverlay(true, 'Iniciando búsqueda...');
        return true;
    }

    (function () {
        var overlay = document.getElementById('pnlSyncOverlay');
        document.addEventListener('keydown', function (event) {
            if (overlay && overlay.classList.contains('is-active') && event.key === 'Tab') {
                event.preventDefault(); overlay.focus();
            }
        }, true);

        var polling = document.getElementById('hidSyncPolling');
        if (!polling || polling.value !== '1') return;

        setGmailSyncOverlay(true, 'Esperando confirmación del estado...');

        var baselineField = document.getElementById('hidSyncBaseline');
        var baseline = baselineField ? parseInt(baselineField.value || '0', 10) : 0;
        var stopped = false;
        var pollingFailures = 0;
        var emptyPolls = 0;
        var syncStatusUrl = '<%= ResolveUrl("~/GmailSyncStatus.ashx") %>';

        function refreshByGet() {
            stopped = true;
            window.location.replace(window.location.pathname + window.location.search);
        }

        function schedule() {
            if (!stopped) window.setTimeout(poll, 2500);
        }

        function pollingFailed() {
            pollingFailures++;
            setGmailSyncOverlay(true, 'Esperando confirmación del estado...');
            if (pollingFailures >= 3) { refreshByGet(); return; }
            schedule();
        }

        function poll() {
            var request = new XMLHttpRequest();
            var handled = false;
            request.open('GET', syncStatusUrl + '?baseline=' + encodeURIComponent(baseline) + '&_=' + new Date().getTime(), true);
            request.timeout = 10000;
            request.onreadystatechange = function () {
                if (request.readyState !== 4 || handled) return;
                handled = true;
                if (request.status !== 200) { pollingFailed(); return; }

                var payload;
                try { payload = JSON.parse(request.responseText); }
                catch (error) { pollingFailed(); return; }
                if (!payload || payload.Id <= baseline) {
                    emptyPolls++;
                    if (emptyPolls >= 12) { refreshByGet(); return; }
                    schedule();
                    return;
                }

                pollingFailures = 0;
                emptyPolls = 0;
                var status = document.getElementById('syncStatusText');
                if (status) status.textContent = payload.Texto;
                var button = document.getElementById('btnBuscar');

                if (payload.EnEjecucion) {
                    if (button) { button.disabled = true; button.value = 'Buscando…'; }
                    setGmailSyncOverlay(true, payload.Texto);
                    schedule();
                    return;
                }

                if (!payload.EsTerminal) { schedule(); return; }
                stopped = true;
                setGmailSyncOverlay(true, payload.Texto);
                window.setTimeout(refreshByGet, 500);
            };
            request.onerror = function () { if (!handled) { handled = true; pollingFailed(); } };
            request.ontimeout = function () { if (!handled) { handled = true; pollingFailed(); } };
            request.send(null);
        }

        poll();
    }());
</script>
</asp:Content>
