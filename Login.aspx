<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="Login.aspx.cs" Inherits="RecepcionDocumental.Login" %>
<!DOCTYPE html>
<html lang="es">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>Ingresar - Recepción Documental</title>
    <style>
        :root { color-scheme: light; font-family: "Segoe UI", Arial, sans-serif; }
        * { box-sizing: border-box; }
        body { margin: 0; min-height: 100vh; display: grid; place-items: center; background: #f3f5f7; color: #1f2933; }
        .shell { width: min(92vw, 430px); }
        .brand { margin-bottom: 18px; text-align: center; }
        .brand h1 { margin: 0; font-size: 1.75rem; font-weight: 650; }
        .brand p { margin: 7px 0 0; color: #66707a; }
        .card { background: #fff; border: 1px solid #dfe3e8; border-radius: 12px; padding: 30px; box-shadow: 0 14px 35px rgba(31, 41, 51, .08); }
        .field { margin-bottom: 18px; }
        label { display: block; margin-bottom: 7px; font-weight: 600; }
        input[type=text], input[type=password] { width: 100%; padding: 11px 12px; border: 1px solid #b8c1ca; border-radius: 7px; font: inherit; }
        input:focus { outline: 2px solid #8bb8e8; outline-offset: 1px; border-color: #4d8bc9; }
        .button { width: 100%; border: 0; border-radius: 7px; padding: 11px 16px; background: #212529; color: #fff; font: inherit; font-weight: 650; cursor: pointer; }
        .button:hover { background: #343a40; }
        .error { display: block; margin: 0 0 18px; padding: 10px 12px; border-radius: 7px; background: #fff0f0; border: 1px solid #efb5b5; color: #8c1d1d; }
        .footer { text-align: center; color: #78828c; font-size: .88rem; margin-top: 16px; }
    </style>
</head>
<body>
    <main class="shell">
        <div class="brand">
            <h1>Recepción Documental</h1>
            <p>Acceso al sistema</p>
        </div>
        <form id="formLogin" runat="server" class="card" autocomplete="on">
            <asp:Literal ID="litError" runat="server" />
            <div class="field">
                <label for="txtUsuario">Usuario</label>
                <asp:TextBox ID="txtUsuario" runat="server" MaxLength="100" autocomplete="username" />
            </div>
            <div class="field">
                <label for="txtPassword">Contraseña</label>
                <asp:TextBox ID="txtPassword" runat="server" TextMode="Password" MaxLength="256" autocomplete="current-password" />
            </div>
            <asp:Button ID="btnIngresar" runat="server" Text="Ingresar" CssClass="button" OnClick="btnIngresar_Click" />
        </form>
        <div class="footer">Acceso restringido</div>
    </main>
</body>
</html>
