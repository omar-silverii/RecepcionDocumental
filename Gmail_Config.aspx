<%@ Page Title="Configuración Gmail" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" Async="true" CodeBehind="Gmail_Config.aspx.cs" Inherits="RecepcionDocumental.Gmail_Config" %>
<asp:Content ID="BodyContent" ContentPlaceHolderID="MainContent" runat="server"><main>
<header class="page-header"><p class="eyebrow">Gmail</p><h1>Configuración de cuenta</h1></header>
<asp:Panel ID="pnlSuccess" runat="server" Visible="false" CssClass="alert alert-success"><asp:Literal ID="litSuccess" runat="server" /></asp:Panel>
<asp:Panel ID="pnlOAuthError" runat="server" Visible="false" CssClass="alert alert-danger"><asp:Literal ID="litOAuthError" runat="server" /></asp:Panel>
<asp:Panel ID="pnlDatabaseWarning" runat="server" Visible="false" CssClass="alert alert-warning">No se pudo consultar la base. Verificá que estén ejecutados los scripts 001 a 003.</asp:Panel>
<asp:Panel ID="pnlSinCuenta" runat="server" CssClass="empty-state"><h2>Cuenta Gmail no configurada</h2><p class="text-secondary">Conectá una cuenta autorizada de Google Workspace con permiso de solo lectura.</p><asp:Button ID="btnConectar" runat="server" Text="Conectar con Gmail" CssClass="btn btn-primary" OnClick="ConectarGmail_Click" /></asp:Panel>
<asp:Panel ID="pnlCuenta" runat="server" Visible="false" CssClass="card"><div class="card-body">
<h2 class="h5">Cuenta registrada</h2>
<dl class="row detail-list">
<dt class="col-sm-3">Email</dt><dd class="col-sm-9"><asp:Literal ID="litEmail" runat="server" /></dd>
<dt class="col-sm-3">Estado</dt><dd class="col-sm-9"><asp:Literal ID="litEstado" runat="server" /></dd>
<dt class="col-sm-3">Inicio de recepción</dt><dd class="col-sm-9"><asp:Literal ID="litInicioRecepcion" runat="server" /></dd>
<dt class="col-sm-3">Última consulta</dt><dd class="col-sm-9"><asp:Literal ID="litUltimaConsulta" runat="server" /></dd>
</dl>
<asp:Panel ID="pnlInicializar" runat="server" Visible="false" CssClass="alert alert-info">
<p class="mb-2"><strong>La cuenta está autorizada, pero la recepción todavía no tiene punto de inicio.</strong></p>
<p class="mb-3">Al iniciar desde ahora, los mensajes anteriores quedarán fuera del piloto y las próximas búsquedas procesarán únicamente cambios posteriores a este momento.</p>
<asp:Button ID="btnInicializarDesdeAhora" runat="server" Text="Iniciar recepción desde ahora" CssClass="btn btn-primary" OnClick="InicializarDesdeAhora_Click" />
</asp:Panel>
<asp:Button ID="btnReconectar" runat="server" Text="Reconectar Gmail" CssClass="btn btn-outline-primary" OnClick="ConectarGmail_Click" />
</div></asp:Panel>
</main></asp:Content>

