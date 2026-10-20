# Namines Desk — AYRI mikroservis imaji.
# Ana backend'in Dockerfile'i ile hicbir katmani paylasmaz; tek basina build alir.
FROM node:22-alpine AS builder
WORKDIR /app
ARG NAMINES_API=https://130-162-254-97.sslip.io
ARG NAMINES_FRONTEND=https://namines-sigma.vercel.app
ARG DESK_BASE_PATH=/desk
ENV NAMINES_API=$NAMINES_API
ENV NAMINES_FRONTEND=$NAMINES_FRONTEND
ENV DESK_BASE_PATH=$DESK_BASE_PATH
COPY package.json package-lock.json* ./
RUN npm ci --no-audit --no-fund || npm install --no-audit --no-fund
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
COPY --from=builder /app/public ./public
COPY --from=builder /app/.next ./.next
COPY --from=builder /app/node_modules ./node_modules
COPY --from=builder /app/package.json ./package.json
COPY --from=builder /app/next.config.ts ./next.config.ts
EXPOSE 3200
CMD ["npm", "run", "start"]
