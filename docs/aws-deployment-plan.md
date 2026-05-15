# AWS Deployment Plan — virtuallibrary.online

## Scope

Get the app running on AWS: API + web client + database. No observability stack yet. DNS via one.com (same as doggerbank.online — already proven to work).

---

## Architecture

```
             one.com DNS
          CNAME → ALB hostname
                  │
      ┌───────────▼────────────┐
      │  AWS Application LB    │  HTTPS :443 (ACM cert)
      │  ALB Ingress Controller│
      └──────┬──────────┬──────┘
             │          │
      /api/* │          │ /*
             ▼          ▼
         ┌───────┐  ┌────────────┐
         │  api  │  │ web-client │
         │ pods  │  │   pods     │
         └───┬───┘  └────────────┘
             │
             ▼
      ┌──────────────┐
      │  PostgreSQL  │  StatefulSet (same pattern as bank)
      └──────────────┘
```

**Cluster:** share the existing EKS cluster (`eu-central-1`) — add a `virtual-library` namespace. No new control plane cost.

---

## Step 1 — ECR repositories

Create two private repos in `eu-central-1` (same account as bank):

```bash
aws ecr create-repository --repository-name virtualibrary/api        --region eu-central-1
aws ecr create-repository --repository-name virtualibrary/web-client  --region eu-central-1
```

---

## Step 2 — TLS certificate (ACM)

Request a certificate in `eu-central-1`:

1. ACM → Request certificate → Public → add both `virtuallibrary.online` and `www.virtuallibrary.online`.
2. ACM gives you a CNAME record for DNS validation — add it in one.com's DNS panel.
3. Wait ~5 min for validation. Copy the certificate ARN.

---

## Step 3 — Web client Dockerfile

The WASM build compiles to static files; a small nginx container serves them.

`VirtualLibrary.Client/Dockerfile`:
```dockerfile
# Stage 1 — build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet workload install wasm-tools
RUN dotnet publish VirtualLibrary.Client/VirtualLibrary.Client.csproj \
    -f net10.0-browserwasm \
    -c Release \
    -o /publish/wasm

# Stage 2 — serve
FROM nginx:alpine
COPY --from=build /publish/wasm/wwwroot /usr/share/nginx/html
COPY VirtualLibrary.Client/nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 8080
```

`VirtualLibrary.Client/nginx.conf`:
```nginx
server {
    listen 8080;
    root /usr/share/nginx/html;
    index index.html;

    # SPA fallback — all unknown paths serve index.html
    location / {
        try_files $uri $uri/ /index.html;
    }

    # Correct MIME type for WebAssembly + long cache (fingerprinted filenames)
    location ~* \.wasm$ {
        types { application/wasm wasm; }
        add_header Cache-Control "public, max-age=604800, immutable";
    }
}
```

In Release, `ApiClient.GetBaseUri()` already returns `window.location.origin` — the ingress routes `/api/*` to the API service, so no hardcoded URL is needed.

---

## Step 4 — Kubernetes manifests

Create a `k8s/` directory at the repo root. Files below; adapt namespace and image URIs throughout.

### `namespace.yaml`
```yaml
apiVersion: v1
kind: Namespace
metadata:
  name: virtual-library
```

### `postgres-secret.yaml`  *(do not commit — apply manually or via CI)*
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: postgres-secret
  namespace: virtual-library
type: Opaque
stringData:
  POSTGRES_USER: "vlibuser"
  POSTGRES_PASSWORD: "<strong-password>"
  POSTGRES_DB: "virtuallibrary"
```

### `postgres-configmap.yaml`
```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: postgres-config
  namespace: virtual-library
data:
  POSTGRES_HOST: "postgres"
  POSTGRES_PORT: "5432"
```

### `postgres-statefulset.yaml`
Copy `k8s/postgres-statefulset.yaml` from the bank repo. Change:
- `namespace` → `virtual-library`
- `postgres-secret` and `postgres-config` names stay the same
- Database name → `virtuallibrary`
- Storage class → `gp2-csi` (already on the cluster)

### `postgres-service.yaml`
```yaml
apiVersion: v1
kind: Service
metadata:
  name: postgres
  namespace: virtual-library
spec:
  selector:
    app: postgres
  ports:
    - port: 5432
```

### `api-configmap.yaml`
```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: api-config
  namespace: virtual-library
data:
  ASPNETCORE_ENVIRONMENT: "Production"
  ASPNETCORE_URLS: "http://+:5179"
  AllowedOrigins: "https://virtuallibrary.online,https://www.virtuallibrary.online"
  Jwt__Issuer: "https://virtuallibrary.online"
  Jwt__Audience: "https://virtuallibrary.online"
```

### `api-secret.yaml`  *(do not commit — apply manually or via CI)*
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: api-secret
  namespace: virtual-library
type: Opaque
stringData:
  ConnectionStrings__DefaultConnection: "Host=postgres;Database=virtuallibrary;Username=vlibuser;Password=<password>"
  Jwt__Key: "<32-char-random-string>"
  Auth__Google__ClientId: ""
  Auth__Google__ClientSecret: ""
  Auth__Apple__ClientId: ""
```

### `api-deployment.yaml`
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: api
  namespace: virtual-library
spec:
  replicas: 2
  selector:
    matchLabels:
      app: api
  template:
    metadata:
      labels:
        app: api
    spec:
      containers:
        - name: api
          image: <account>.dkr.ecr.eu-central-1.amazonaws.com/virtualibrary/api:latest
          imagePullPolicy: Always
          ports:
            - containerPort: 5179
          envFrom:
            - configMapRef:
                name: api-config
            - secretRef:
                name: api-secret
          resources:
            requests:
              memory: "256Mi"
              cpu: "100m"
            limits:
              memory: "512Mi"
              cpu: "500m"
          readinessProbe:
            httpGet:
              path: /health
              port: 5179
            initialDelaySeconds: 30
            periodSeconds: 10
          livenessProbe:
            httpGet:
              path: /health
              port: 5179
            initialDelaySeconds: 45
            periodSeconds: 15
```

### `api-service.yaml`
```yaml
apiVersion: v1
kind: Service
metadata:
  name: api
  namespace: virtual-library
spec:
  selector:
    app: api
  ports:
    - port: 5179
      targetPort: 5179
```

### `web-client-deployment.yaml`
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: web-client
  namespace: virtual-library
spec:
  replicas: 2
  selector:
    matchLabels:
      app: web-client
  template:
    metadata:
      labels:
        app: web-client
    spec:
      containers:
        - name: web-client
          image: <account>.dkr.ecr.eu-central-1.amazonaws.com/virtualibrary/web-client:latest
          imagePullPolicy: Always
          ports:
            - containerPort: 8080
          resources:
            requests:
              memory: "64Mi"
              cpu: "50m"
            limits:
              memory: "128Mi"
              cpu: "100m"
          readinessProbe:
            httpGet:
              path: /
              port: 8080
            initialDelaySeconds: 5
            periodSeconds: 10
          livenessProbe:
            httpGet:
              path: /
              port: 8080
            initialDelaySeconds: 10
            periodSeconds: 15
```

### `web-client-service.yaml`
```yaml
apiVersion: v1
kind: Service
metadata:
  name: web-client
  namespace: virtual-library
spec:
  selector:
    app: web-client
  ports:
    - port: 8080
      targetPort: 8080
```

### `ingress.yaml`
Same ALB annotations as the bank — only the cert ARN and hostnames differ:
```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: virtualibrary-ingress
  namespace: virtual-library
  annotations:
    kubernetes.io/ingress.class: alb
    alb.ingress.kubernetes.io/scheme: internet-facing
    alb.ingress.kubernetes.io/target-type: ip
    alb.ingress.kubernetes.io/listen-ports: '[{"HTTP":80},{"HTTPS":443}]'
    alb.ingress.kubernetes.io/ssl-redirect: "443"
    alb.ingress.kubernetes.io/certificate-arn: "arn:aws:acm:eu-central-1:<account>:certificate/<cert-id>"
spec:
  rules:
    - host: virtuallibrary.online
      http:
        paths:
          - path: /api
            pathType: Prefix
            backend:
              service:
                name: api
                port:
                  number: 5179
          - path: /
            pathType: Prefix
            backend:
              service:
                name: web-client
                port:
                  number: 8080
    - host: www.virtuallibrary.online
      http:
        paths:
          - path: /api
            pathType: Prefix
            backend:
              service:
                name: api
                port:
                  number: 5179
          - path: /
            pathType: Prefix
            backend:
              service:
                name: web-client
                port:
                  number: 8080
```

---

## Step 5 — DNS at one.com

Once the ingress is created, AWS provisions an ALB with a hostname like:
`k8s-virtuallibrary-xxx.eu-central-1.elb.amazonaws.com`

In one.com DNS panel — same approach as doggerbank.online:

| Type | Name | Value |
|---|---|---|
| CNAME | `www` | `k8s-virtuallibrary-xxx.eu-central-1.elb.amazonaws.com` |
| CNAME / ALIAS | `@` | same ALB hostname (use ALIAS if one.com supports it, otherwise redirect naked → www) |

---

## Step 6 — CI/CD

Copy `.github/workflows/ci-cd.yml` from the bank repo. Changes:
- ECR repo names: `virtualibrary/api`, `virtualibrary/web-client`
- Cluster name: existing cluster name
- API build: `dotnet publish -c Release` instead of Maven
- Web client build: `docker buildx build` using the new Dockerfile in `VirtualLibrary.Client/`
- Namespace in `kubectl` commands: `virtual-library`

The OIDC role assumption is identical — add `virtualibrary/*` to the ECR policy on the existing role, or create a new role for this repo.

---

## Implementation order

1. **ACM cert** — request it first; validation runs in the background.
2. **ECR repos** — two commands, done in 2 minutes.
3. **Web client Dockerfile + nginx.conf** — write and test locally with `docker build`.
4. **k8s manifests** — write, then apply manually: `kubectl apply -f k8s/`
5. **Secrets** — `kubectl apply -f api-secret.yaml` and `postgres-secret.yaml` (outside git).
6. **DNS** — add CNAME once ALB hostname is known.
7. **CI/CD** — wire up after a manual deploy confirms everything works.

---

## Incremental additions (later)

- **HPA** — copy `api-hpa.yaml` and `web-client-hpa.yaml` from bank when you want autoscaling.
- **PDB** — copy `api-pdb.yaml` and `web-client-pdb.yaml` for zero-downtime rolling deploys.
- **RDS** — swap the StatefulSet for an RDS instance when you want managed backups.
- **Observability** — drop in the Prometheus/Grafana/Loki/Tempo stack from the bank when you want metrics and logs.
