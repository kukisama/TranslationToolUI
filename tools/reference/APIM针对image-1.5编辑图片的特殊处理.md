# APIM 针对 gpt-image-1.5 编辑图片（images/edits）的特殊处理

> 适用场景：
> - 客户端使用 OpenAI 兼容接口（如 `POST /v1/images/edits`）
> - 经过 one-api / 网关后，上游接入 Microsoft APIM
> - APIM 后端最终对接 Azure OpenAI（AOAI）

---

## 1. generations 和 edits 分别是什么

为了让这个工作的必要性更直观，先用业务语言解释两个接口：

- `POST /v1/images/generations`
  - 功能：**文生图**。输入提示词，模型从零开始生成图片。
  - 典型场景：营销海报、概念图、风格图从 0 到 1。

- `POST /v1/images/edits`
  - 功能：**基于参考图编辑**。输入提示词 + 一张或多张参考图，模型在原图基础上修改。
  - 典型场景：换背景、换风格、局部改造、多图融合。

为什么这件事必须做：

- 如果只有 generations 可用，那只能“重画一张”，很难稳定保留原图主体。
- 实际业务里“保留主体并修改”是高频需求，必须依赖 edits。

---

## 2. 背景与问题现象

可以把这件事理解为一句话：

**我们当前引入 APIM，不是为了“多一层复杂度”，而是为了在 AOAI 权限策略收紧（逐步从 key 模式转向 AAD）的情况下，保留可控的兼容接入能力。**


- 微软这边整体趋势是收紧 AOAI 的 key 直连权限，更多要求 AAD。
- 业务侧又有历史系统和网关链路依赖 key 模式，短期不可能一次性全改。
- 所以 APIM 是折中方案：通过 APIM 这一层做统一治理，再把外部兼容接入“放”出来。

本次排查中，现场现象非常典型：

- `POST /v1/images/generations` 可用（200）
- `POST /v1/images/edits` 不可用（404）

排查结论也很明确：

1. 客户端请求是正确的（兼容接口、multipart、字段都没问题）。
2. 404 不是参数校验类错误（参数问题通常是 400/422）。
3. 问题在网关/上游映射层，尤其是 APIM 对 `images/edits` 的 operation/policy 未与 generations 同步完善。

---

## 3. 为什么要做“特殊处理”

AOAI 的图像编辑路径通常是部署级接口：

`/openai/deployments/{deployment-id}/images/edits?api-version=...`

而很多客户端或网关入口使用的是 OpenAI 兼容路径：

`/v1/images/edits`

如果 APIM 只配置了 generations 的 operation/policy，而没有针对 edits 做同等 rewrite，就会出现：

- generations 正常
- edits 404（Resource not found）

因此需要在 APIM 中为 edits 增加**独立 operation + 路径重写 + query 补齐 + 认证透传/注入**。

> 说明：本次实施不是“从零重写 APIM API”，而是基于现有 DALL 可用的 ` /v1/images/generations ` operation 做复制扩展，图形界面操作即可完成大部分配置，工程改动相对可控。

---

## 4. 推荐目标架构

客户端（OpenAI兼容）
→ one-api（可选）
→ APIM（统一治理）
→ AOAI（实际推理）

其中 APIM 至少要完整覆盖：

- `POST /v1/images/generations`
- `POST /v1/images/edits`

并且两者都要有对应 rewrite 与 policy。

---

## 5. APIM 需要的关键配置项

### 4.1 Operation 设计（最小集合）

建议在 APIM API 下显式创建这两个 Operation：

1. `POST /v1/images/generations`
2. `POST /v1/images/edits`

> 注意：不要只创建 generations。edits 必须有自己的 operation。

推荐做法（图形界面最省事）：

1. 找到当前已可用的 `POST /v1/images/generations`（DALL 那条）。
2. 复制该 operation 生成一条 `POST /v1/images/edits`。
3. 保留原有认证、产品、订阅策略。
4. 仅替换路径重写与必要参数（见下节）。

### 4.2 edits 的路径重写（核心）

把前端兼容路径重写到 AOAI 部署路径，例如：

- 入站：`/v1/images/edits`
- 出站到后端：`/openai/deployments/{deployment-id}/images/edits?api-version=2024-02-01`

其中：

- `{deployment-id}` 对应图像模型部署（如 `gpt-image-1.5`）
- `api-version` 建议固定或由参数控制，但必须存在

### 4.3 Query 参数补齐

若前端未传 `api-version`，可在 APIM inbound 中补写：

- `api-version=2024-02-01`

### 4.4 认证策略

按你的网关设计二选一：

1. one-api 传 Bearer 到 APIM，再由 APIM 转 AOAI
2. APIM 使用后端命名值（api-key）注入到 AOAI

重点是确保 edits operation 与 generations operation 使用一致且正确的认证流程。

---

## 6. 示例策略（edits operation）

> 以下为示例模板，请按你的 APIM 实际变量/命名值调整。

```xml
<policies>
  <inbound>
    <base />

    <!-- 可选：固定后端服务地址 -->
    <set-backend-service base-url="https://<your-aoai-endpoint>.cognitiveservices.azure.com" />

    <!-- 将兼容路径重写到 AOAI 部署路径 -->
    <rewrite-uri template="/openai/deployments/{{deployment-id}}/images/edits" copy-unmatched-params="false" />

    <!-- 补齐 api-version -->
    <set-query-parameter name="api-version" exists-action="override">
      <value>2024-02-01</value>
    </set-query-parameter>

    <!-- 若采用 APIM 注入后端 Key（示例） -->
    <!--
    <set-header name="api-key" exists-action="override">
      <value>{{aoai-api-key}}</value>
    </set-header>
    -->
  </inbound>

  <backend>
    <base />
  </backend>

  <outbound>
    <base />
  </outbound>

  <on-error>
    <base />
  </on-error>
</policies>
```

---

## 7. 问题的定位经验

### 6.1 关键证据

- 失败时：`X-Oneapi-Request-Id` 存在，404
- 成功时：`Apim-Request-Id`、`X-Ms-Deployment-Name`、`X-Ms-Region` 存在，200

这说明：

- 失败请求被 one-api 接住，但未正确命中 APIM→AOAI 的可用链路
- 成功请求确实到达 AOAI

### 6.2 一句话诊断法

若出现“generations 可以、edits 不行”，优先检查 APIM 是否缺少 edits operation/policy。

补充一条实操判断：

- 如果 generations 一直正常，edits 稳定 404，优先检查 APIM 是否“只复制了 operation 但没改 rewrite-uri”。

---

## 8. 联调与验收清单

按以下顺序检查：

1. APIM 中是否存在 `POST /v1/images/edits` operation
2. edits operation 是否有 rewrite 到 `/openai/deployments/{deployment}/images/edits`
3. 是否写入 `api-version=2024-02-01`
4. 认证策略是否与 generations 一致且有效
5. 产品/订阅是否允许访问 edits operation
6. 用同一 key 重放请求，核对 APIM trace 与 one-api 请求号

通过标准：

- `POST /v1/images/edits` 返回 200
- 响应头可见 APIM/AOAI 标识（如 `Apim-Request-Id` / `X-Ms-Deployment-Name`）

---

## 9. 与本项目当前实现的对应关系

本项目图片编辑已按兼容模式发送：

- URL：`/v1/images/edits`
- 方法：`POST`
- Body：`multipart/form-data`
- 字段：`prompt`、`model`、`image`（支持多图同名 `image`）

因此，客户端侧不再是主要瓶颈；关键在网关/APIM 映射完整性。

---

## 10. 运维建议

1. 在 one-api 与 APIM 间打通请求号关联（记录 `X-Oneapi-Request-Id` 与 `Apim-Request-Id`）
2. 将 edits 与 generations 的 policy 做“成对维护”
3. 每次变更后都执行回归：
   - 单图编辑
   - 多图编辑
   - 纯文生图

这样可以避免“某条路径可用、另一条路径失效”的隐性回归。
