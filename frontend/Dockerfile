# ─── Stage 1: Dependencies ───────────────────────────────────────────────────
FROM node:22-alpine AS deps
WORKDIR /app

COPY package.json package-lock.json* ./
RUN npm ci --frozen-lockfile --no-audit --no-fund

# ─── Stage 2: Builder ────────────────────────────────────────────────────────
FROM node:22-alpine AS builder
WORKDIR /app

# Build-time API URL (docker-compose build args'dan gelir).
# DİKKAT: '/api' YAZMA — services/api.ts bu değerin sonuna '/api' ekler; iki kez
# yazılırsa istekler '.../api/api/...' adresine gider.
ARG NEXT_PUBLIC_API_URL=http://localhost:5000
ENV NEXT_PUBLIC_API_URL=$NEXT_PUBLIC_API_URL

# PostHog: NEXT_PUBLIC_* değerleri build sırasında bundle'a gömülür; runtime'da
# `environment:` ile vermek istemci koduna ULAŞMAZ. Bu yüzden build arg olarak alınır.
ARG NEXT_PUBLIC_POSTHOG_KEY=
ARG NEXT_PUBLIC_POSTHOG_HOST=/ingest
ENV NEXT_PUBLIC_POSTHOG_KEY=$NEXT_PUBLIC_POSTHOG_KEY
ENV NEXT_PUBLIC_POSTHOG_HOST=$NEXT_PUBLIC_POSTHOG_HOST

COPY --from=deps /app/node_modules ./node_modules
COPY . .

RUN npm run build

# ─── Stage 3: Production Runtime ─────────────────────────────────────────────
FROM node:22-alpine AS runner
WORKDIR /app

ENV NODE_ENV=production
ENV PORT=3000
ENV HOSTNAME=0.0.0.0

# Sadece production için gerekli dosyalar
COPY --from=builder --chown=node:node /app/public ./public
COPY --from=builder --chown=node:node /app/.next/standalone ./
COPY --from=builder --chown=node:node /app/.next/static ./.next/static

# Root olmayan süreç (node:alpine'in hazır `node` kullanıcısı).
USER node

EXPOSE 3000

CMD ["node", "server.js"]
