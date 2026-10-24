/* global SwaggerUIBundle */
window.addEventListener("load", () => {
  SwaggerUIBundle({
    url: "/api/openapi.json",
    dom_id: "#swagger-ui",
    deepLinking: true,
    persistAuthorization: false,
    validatorUrl: null,
    tryItOutEnabled: false,
  });
});
