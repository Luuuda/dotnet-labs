/* =====================================================================
   Проєкт №3 — MongoDB «cinema_reviews» (Відгуки та обговорення)
   01_schema.js — колекції, JSON Schema Validation, індекси.
   Запуск:  mongosh "mongodb://localhost:27017" 01_schema.js
   Ідемпотентний: якщо колекція вже існує, валідатор оновлюється (collMod),
   createIndex для наявного індексу нічого не робить.

   Колекції:
     reviews          — відгук + ВБУДОВАНІ коментарі (embedding)
     discussions      — тема обговорення + вбудовані 3 останні пости (ГІБРИД)
     discussionPosts  — усі пости теми, ПОСИЛАННЯ discussionId (referencing,
                        необмежений ріст)
     movieRatings     — денормалізований агрегат рейтингу фільму (для читання)
   ===================================================================== */
db = db.getSiblingDB("cinema_reviews");

const author = {
  bsonType: "object",
  required: ["userId", "displayName"],
  properties: {
    userId:      { bsonType: "string", description: "Customers.Id з CinemaBooking (GUID)" },
    displayName: { bsonType: "string", minLength: 1, maxLength: 100 }
  }
};

const schemas = {
  reviews: {
    $jsonSchema: {
      bsonType: "object",
      required: ["movieId", "movieTitle", "author", "rating", "createdAt"],
      properties: {
        movieId:    { bsonType: "int", minimum: 1, description: "Movies.Id з CinemaCatalog — посилання без FK" },
        movieTitle: { bsonType: "string", description: "денормалізовано з Каталогу для читання" },
        author,
        rating:     { bsonType: "int", minimum: 1, maximum: 5, description: "ціла оцінка 1..5" },
        text:       { bsonType: "string", maxLength: 5000 },
        verifiedViewer: { bsonType: "bool", description: "true, якщо є виданий квиток (подія з Бронювання)" },
        bookingId:  { bsonType: ["long", "int"] },
        aspects: {
          bsonType: "object",
          additionalProperties: { bsonType: "int", minimum: 1, maximum: 5 }
        },
        photos:   { bsonType: "array", items: { bsonType: "string" } },
        comments: {
          bsonType: "array",
          maxItems: 50,                         // вбудовуємо лише обмежену кількість
          items: {
            bsonType: "object",
            required: ["_id", "author", "text", "createdAt"],
            properties: {
              _id: { bsonType: "objectId" },
              author,
              text: { bsonType: "string", minLength: 1, maxLength: 1000 },
              createdAt: { bsonType: "date" }
            }
          }
        },
        commentsCount: { bsonType: "int", minimum: 0 },
        discussionId:  { bsonType: "objectId", description: "посилання на discussions" },
        createdAt: { bsonType: "date" },
        updatedAt: { bsonType: "date" }
      }
    }
  },

  discussions: {
    $jsonSchema: {
      bsonType: "object",
      required: ["movieId", "movieTitle", "title", "createdBy", "status", "postsCount", "createdAt"],
      properties: {
        movieId:    { bsonType: "int", minimum: 1 },
        movieTitle: { bsonType: "string" },
        title:      { bsonType: "string", minLength: 3, maxLength: 200 },
        createdBy:  author,
        status:     { enum: ["open", "closed"] },
        postsCount: { bsonType: "int", minimum: 0 },
        lastPosts:  { bsonType: "array", maxItems: 3 },     // гібрид: копія 3 останніх постів
        tags:       { bsonType: "array", items: { bsonType: "string" } },
        poll: {
          bsonType: "object",
          required: ["question", "options"],
          properties: {
            question: { bsonType: "string" },
            options:  { bsonType: "array", minItems: 2 }
          }
        },
        createdAt:      { bsonType: "date" },
        lastActivityAt: { bsonType: "date" }
      }
    }
  },

  discussionPosts: {
    $jsonSchema: {
      bsonType: "object",
      required: ["discussionId", "author", "text", "createdAt"],
      properties: {
        discussionId:  { bsonType: "objectId" },
        author,
        text:          { bsonType: "string", minLength: 1, maxLength: 10000 },
        replyToPostId: { bsonType: "objectId" },
        reactions:     { bsonType: "object", additionalProperties: { bsonType: "int", minimum: 0 } },
        createdAt:     { bsonType: "date" },
        editedAt:      { bsonType: "date" }
      }
    }
  },

  movieRatings: {
    $jsonSchema: {
      bsonType: "object",
      required: ["_id", "movieTitle", "average", "count", "distribution", "updatedAt"],
      properties: {
        _id:          { bsonType: "int", description: "= movieId" },
        movieTitle:   { bsonType: "string" },
        average:      { bsonType: ["double", "int", "decimal"], minimum: 1, maximum: 5 },
        count:        { bsonType: ["int", "long"], minimum: 1 },
        distribution: { bsonType: "object" },
        updatedAt:    { bsonType: "date" }
      }
    }
  }
};

const existing = db.getCollectionNames();
for (const [name, validator] of Object.entries(schemas)) {
  if (existing.includes(name)) {
    db.runCommand({ collMod: name, validator, validationLevel: "strict", validationAction: "error" });
    print(`collMod: ${name} — валідатор оновлено`);
  } else {
    db.createCollection(name, { validator, validationLevel: "strict", validationAction: "error" });
    print(`createCollection: ${name}`);
  }
}

/* ---------------- Індекси (кожен — під конкретний запит) ---------------- */
// «відгуки фільму, нові зверху, з пагінацією»
db.reviews.createIndex({ movieId: 1, createdAt: -1 }, { name: "ix_movie_createdAt" });
// «один користувач — один відгук на фільм» + «мої відгуки»
db.reviews.createIndex({ "author.userId": 1, movieId: 1 }, { name: "ux_author_movie", unique: true });
// повнотекстовий пошук по тексту відгуку
db.reviews.createIndex({ text: "text" }, { name: "tx_text", default_language: "none" });
// обробник події MovieRenamed: updateMany({movieId}) у кожній колекції використовує індекс за movieId
// «теми обговорень фільму, найактивніші зверху»
db.discussions.createIndex({ movieId: 1, lastActivityAt: -1 }, { name: "ix_movie_activity" });
// «пости теми в хронологічному порядку з пагінацією»
db.discussionPosts.createIndex({ discussionId: 1, createdAt: 1 }, { name: "ix_discussion_createdAt" });
// «топ фільмів за рейтингом»
db.movieRatings.createIndex({ average: -1, count: -1 }, { name: "ix_average" });

print("cinema_reviews: схему й індекси готово.");
