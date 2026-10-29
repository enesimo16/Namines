# Namines Desk — AYRI mikroservis imaji.
# Ana backend'in Dockerfile'i ile hicbir katmani paylasmaz; tek basina build alir.
FROM node:22-alpine AS builder
WORKDIR /app
ARG NAMINES_API=https://130-162-254-97.sslip.io
ARG NAMINES_FRONTEND=https://namines-sigma.vercel.app
ARG NAMINES_PUBLIC_API
ENV NAMINES_PUBLIC_API=$NAMINES_PUBLIC_API
ARG DESK_BASE_PATH=/desk
ENV NAMINES_API=$NAMINES_API
ENV NAMINES_FRONTEND=$NAMINES_FRONTEND
ENV DESK_BASE_PATH=$DESK_BASE_PATH
COPY package.json package-lock.json* ./
RUN npm ci --no-audit --no-fund
COPY . .
RUN npm run build

FROM node:22-alpine AS runner
WORKDIR /app
ENV NODE_ENV=production
ARG NAMINES_API=https://130-162-254-97.sslip.io
ARG NAMINES_FRONTEND=https://namines-sigma.vercel.app
ARG DESK_BASE_PATH=/desk
ENV NAMINES_API=$NAMINES_API
ENV NAMINES_FRONTEND=$NAMINES_FRONTEND
ENV DESK_BASE_PATH=$DESK_BASE_PATH
COPY --from=builder --chown=node:node /app/public ./public
COPY --from=builder --chown=node:node /app/.next ./.next
COPY --from=builder --chown=node:node /app/node_modules ./node_modules
COPY --from=builder --chown=node:node /app/package.json ./package.json
COPY --from=builder --chown=node:node /app/next.config.ts ./next.config.ts
# Root olmayan süreç (node:alpine'in hazır `node` kullanıcısı); `next start` .next/cache'e yazar.
USER node

EXPOSE 3200
CMD ["npm", "run", "start"]
