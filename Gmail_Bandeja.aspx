<%@ Page Title="Bandeja Gmail" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" Async="true" AsyncTimeout="600" CodeBehind="Gmail_Bandeja.aspx.cs" Inherits="RecepcionDocumental.Gmail_Bandeja" %>
<%@ Register Src="~/Controls/WsListadoAgrupado.ascx" TagPrefix="ws" TagName="ListadoAgrupado" %>
<asp:Content ID="BodyContent" ContentPlaceHolderID="MainContent" runat="server">
<main class="gmail-inbox-page">
    <header class="page-header d-flex justify-content-between align-items-end flex-wrap gap-3">
        <div>
            <p class="eyebrow">Gmail</p>
            <h1>Bandeja de entrada</h1>
        </div>
        <div class="d-flex gap-2">
            <asp:Button ID="btnBuscar" runat="server" Text="Buscar nuevos correos" CssClass="btn btn-primary" UseSubmitBehavior="false" OnClientClick="this.disabled=true;this.value='Buscando…';" OnClick="Buscar_Click" />
            <a href="Gmail_Config.aspx" class="btn btn-outline-primary">Configurar cuenta</a>
        </div>
    </header>

    <asp:Panel ID="pnlDatabaseWarning" runat="server" Visible="false" CssClass="alert alert-warning">
        <asp:Literal ID="litDatabaseWarning" runat="server" />
    </asp:Panel>

    <p class="text-secondary mb-3"><asp:Literal ID="litSyncStatus" runat="server" /></p>

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
</asp:Content>
