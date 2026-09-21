<%@ Page Title="Bandeja Gmail" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="Gmail_Bandeja.aspx.cs" Inherits="RecepcionDocumental.Gmail_Bandeja" %>
<%@ Register Src="~/Controls/WsListadoAgrupado.ascx" TagPrefix="ws" TagName="ListadoAgrupado" %>
<asp:Content ID="BodyContent" ContentPlaceHolderID="MainContent" runat="server">
<main class="gmail-inbox-page">
    <header class="page-header d-flex justify-content-between align-items-end flex-wrap gap-3">
        <div>
            <p class="eyebrow">Gmail</p>
            <h1>Bandeja de entrada</h1>
        </div>
        <div class="d-flex gap-2">
            <asp:Button ID="btnBuscar" runat="server" ClientIDMode="Static" Text="Buscar nuevos correos" CssClass="btn btn-primary" UseSubmitBehavior="false" OnClientClick="this.disabled=true;this.value='Buscando…';" OnClick="Buscar_Click" />
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
        <h2 class="h5">Sincronización terminada</h2>
        <ul class="mb-0">
            <li>Mensajes encontrados: <asp:Literal ID="litEncontrados" runat="server" /></li>
            <li>Mensajes relevantes nuevos: <asp:Literal ID="litNuevos" runat="server" /></li>
            <li>Adjuntos analizados: <asp:Literal ID="litAnalizados" runat="server" /></li>
            <li>Facturas detectadas: <asp:Literal ID="litFacturas" runat="server" /></li>
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
    (function () {
        var polling = document.getElementById('hidSyncPolling');
        if (!polling || polling.value !== '1') return;

        var baselineField = document.getElementById('hidSyncBaseline');
        var baseline = baselineField ? parseInt(baselineField.value || '0', 10) : 0;
        var stopped = false;

        function schedule() {
            if (!stopped) window.setTimeout(poll, 2500);
        }

        function poll() {
            var request = new XMLHttpRequest();
            request.open('POST', 'Gmail_Bandeja.aspx/GetSyncStatus', true);
            request.setRequestHeader('Content-Type', 'application/json; charset=utf-8');
            request.onreadystatechange = function () {
                if (request.readyState !== 4) return;
                if (request.status !== 200) { schedule(); return; }

                var payload;
                try { payload = JSON.parse(request.responseText).d; }
                catch (error) { schedule(); return; }
                if (!payload || payload.Id <= baseline) { schedule(); return; }

                var status = document.getElementById('syncStatusText');
                if (status) status.textContent = payload.Texto;
                var button = document.getElementById('btnBuscar');

                if (payload.EnEjecucion) {
                    if (button) { button.disabled = true; button.value = 'Buscando…'; }
                    schedule();
                    return;
                }

                stopped = true;
                if (button) { button.disabled = false; button.value = 'Buscar nuevos correos'; }
                var notice = document.getElementById('pnlDatabaseWarning');
                if (notice) notice.style.display = 'none';
                window.setTimeout(function () { window.location.reload(); }, 500);
            };
            request.send('{}');
        }

        poll();
    }());
</script>
</asp:Content>
