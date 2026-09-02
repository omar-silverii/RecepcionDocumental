<%@ Page Title="Revisión documental" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="Documento_Revisar.aspx.cs" Inherits="RecepcionDocumental.Documento_Revisar" %>
<asp:Content ID="BodyContent" ContentPlaceHolderID="MainContent" runat="server">
<main class="review-station">
    <header class="page-header d-flex flex-column flex-md-row justify-content-between align-items-md-end gap-2">
        <div><p class="eyebrow">Ground truth humano</p><h1><%= IsSample ? "Revisión de control" : "Revisión documental" %></h1><asp:Literal ID="litPosition" runat="server" /></div>
        <a class="btn btn-outline-secondary" href="Documentos.aspx">Volver a lista</a>
    </header>
    <asp:Panel ID="pnlMessage" runat="server" Visible="false" CssClass="alert alert-info"><asp:Literal ID="litMessage" runat="server" /> <asp:HyperLink ID="lnkContinue" runat="server" Visible="false" CssClass="alert-link">Continuar con el siguiente pendiente</asp:HyperLink></asp:Panel>
    <asp:Panel ID="pnlEmpty" runat="server" Visible="false" CssClass="empty-state"><h2>No quedan documentos pendientes.</h2><p class="text-secondary mb-3">La cola de revisión está completa.</p><a class="btn btn-outline-primary" href="Documentos.aspx?clasificacion=REVISAR">Volver a documentos</a></asp:Panel>
    <asp:Panel ID="pnlWork" runat="server" Visible="false">
        <div class="review-layout">
            <section class="review-preview card border-0 shadow-sm">
                <asp:Panel ID="pnlInline" runat="server" Visible="false" CssClass="review-frame-wrap"><iframe id="documentPreview" title="Vista previa del documento" src="<%= PreviewUrl %>" class="review-frame"></iframe></asp:Panel>
                <asp:Panel ID="pnlDownload" runat="server" Visible="false" CssClass="review-download"><div><h2>Vista previa no disponible</h2><p>Este formato no se puede mostrar dentro de la página. Podés abrirlo o descargarlo sin alterar el documento.</p><asp:HyperLink ID="lnkOpenDocument" runat="server" Target="_blank" CssClass="btn btn-primary">Abrir / Descargar</asp:HyperLink></div></asp:Panel>
            </section>
            <aside class="review-decision card border-0 shadow-sm p-4">
                <h2 class="h5 mb-3">Decisión humana</h2>
                <dl class="detail-list review-details">
                    <dt>Documento</dt><dd><asp:Literal ID="litName" runat="server" /></dd>
                    <dt>Fecha</dt><dd><asp:Literal ID="litDate" runat="server" /></dd>
                    <dt>Remitente</dt><dd><asp:Literal ID="litSender" runat="server" /></dd>
                    <dt>Asunto</dt><dd><asp:Literal ID="litSubject" runat="server" /></dd>
                    <asp:PlaceHolder ID="phAutomatic" runat="server" EnableViewState="false">
                    <dt>Clasificación automática</dt><dd><asp:Literal ID="litClassification" runat="server" /></dd>
                    <dt>Método</dt><dd><asp:Literal ID="litMethod" runat="server" /></dd>
                    <dt>Confianza</dt><dd><asp:Literal ID="litConfidence" runat="server" /></dd>
                    <dt>Motivo</dt><dd><asp:Literal ID="litReason" runat="server" /></dd>
                    </asp:PlaceHolder>
                </dl>
                <label for="<%= txtObservation.ClientID %>" class="form-label fw-semibold">Observación opcional</label>
                <asp:TextBox ID="txtObservation" runat="server" TextMode="MultiLine" Rows="3" MaxLength="1000" CssClass="form-control review-observation" />
                <asp:HiddenField ID="hfDocumentId" runat="server" />
                <div class="d-grid gap-2 mt-3">
                    <asp:Button ID="btnInvoice" runat="server" Text="Confirmar factura" CssClass="btn btn-success" OnClick="Resolve_Click" CommandName="FACTURA" OnClientClick="return confirm('¿Confirmar este documento como factura?');" />
                    <asp:Button ID="btnOther" runat="server" Text="Otro documento" CssClass="btn btn-outline-warning" OnClick="Resolve_Click" CommandName="OTRO_DOCUMENTO" OnClientClick="return confirm('¿Etiquetar como otro documento?');" />
                    <asp:Button ID="btnNonDocument" runat="server" Text="No es un documento" CssClass="btn btn-outline-danger" OnClick="Resolve_Click" CommandName="NO_DOCUMENTO" OnClientClick="return confirm('¿Etiquetar como no-documento?');" />
                </div>
                <nav class="d-flex justify-content-between gap-2 mt-4" aria-label="Navegación de pendientes"><asp:HyperLink ID="lnkPrevious" runat="server" CssClass="btn btn-sm btn-outline-secondary">Anterior pendiente</asp:HyperLink><asp:HyperLink ID="lnkNext" runat="server" CssClass="btn btn-sm btn-outline-secondary">Siguiente pendiente</asp:HyperLink></nav>
            </aside>
        </div>
    </asp:Panel>
</main>
</asp:Content>
