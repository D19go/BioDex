# Biodex social (backend em preparação)

Serviço da rede social do BioDex: contas anônimas, amizades, posts com fotos e feed.
É separado do `FaunaLens/Server` (identificação online com BioCLIP 2, que usa PyTorch).

## Rodar

```bash
pip install -r requirements.txt
SOCIAL_DATA_DIR=./data uvicorn app:app --reload
python -m pytest tests -q
```

## Regras que o serviço garante

- **Sem localização.** Os corpos das requisições rejeitam campos extras. Nenhuma coordenada é aceita ou guardada.
- **Fotos sem metadados.** Cada foto é reescrita em JPEG (até 1600 px), com a rotação aplicada e sem EXIF.
  Fotos de celular trazem GPS; sem isso, um post revelaria onde o animal foi visto.
- **Conta anônima.** `POST /v1/devices` devolve um token. O servidor guarda só o hash SHA-256.
- **Visibilidade.** Post `friends` só aparece para o autor e amigos aceitos (responde 404 para os outros).
  Post `public` aparece no feed público, com o nome do autor.
- **Exclusão.** `DELETE /v1/me` apaga a conta, os posts e as fotos.

## Feed

Amigos e público intercalados: 10 posts de amigos para cada 1 público (`feed.PUBLIC_EVERY = 11`).
A paginação tem um cursor por lista (`beforeFriends`, `beforePublic`), para não pular posts quando as duas
listas andam em ritmos diferentes.

## Endpoints

| Método | Caminho | Função |
|---|---|---|
| POST | /v1/devices | cria conta anônima e devolve o token |
| GET/PUT | /v1/me | lê e muda o nome de exibição |
| DELETE | /v1/me | apaga a conta e tudo que publicou |
| GET | /v1/friends | amigos e pedidos recebidos |
| POST | /v1/friends | pede amizade pelo código de 8 caracteres |
| POST | /v1/friends/{id}/accept | aceita um pedido |
| DELETE | /v1/friends/{id} | desfaz a amizade |
| POST | /v1/posts | publica de 1 a 4 fotos (multipart) |
| GET | /v1/feed | feed paginado |
| GET/DELETE | /v1/posts/{id} | lê ou apaga (só o autor apaga) |
| GET | /v1/posts/{id}/images/{n} | foto (só quem pode ver o post) |
| POST | /v1/reports | denúncia de post |

## Ainda não decidido

- **Hospedagem e custo.** SQLite e pasta de fotos funcionam para testes; produção precisa de banco e armazenamento de objetos.
- **Login.** Hoje é só token anônimo. Para trocar de aparelho ou recuperar a conta, é preciso decidir entre e-mail, Google ou código.
- **Limite de requisições e moderação.** Ainda não há limite por IP/conta. Denúncias são só contadas, não escondem o post.
- **Pontuação.** Os pontos são enviados pelo app e aceitos como estão. Um cliente adulterado pode inflar o ranking.
- **Sincronização com o app.** O app ainda não envia nada para este serviço; a fila de envio (funcionar offline e sincronizar depois) fica para a próxima etapa.
